using System.Globalization;
using System.Text;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Power;
using LidGuard.Runtime;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class LiveStatusCommand
{
    private const int HookEventDisplayLineCount = 8;
    private const int FlowEventDisplayLineCount = 14;
    private const string EnterAlternateScreenSequence = "\u001b[?1049h\u001b[H\u001b[2J";
    private const string ExitAlternateScreenSequence = "\u001b[?1049l";
    private const string StyleResetSequence = "\u001b[0m";
    private const string StyleBoldSequence = "\u001b[1m";
    private const string StyleDimSequence = "\u001b[2m";
    private const string StyleRedSequence = "\u001b[31m";
    private const string StyleGreenSequence = "\u001b[32m";
    private const string StyleYellowSequence = "\u001b[33m";
    private const string StyleCyanSequence = "\u001b[36m";
    private static readonly TimeSpan s_keyPollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan s_runtimeReconnectInterval = TimeSpan.FromSeconds(1);

    public static async Task<int> RunAsync(string[] commandLineArguments)
    {
        if (commandLineArguments.Length > 0)
        {
            Console.Error.WriteLine(LidGuardText.CommandUnexpectedArgument(commandLineArguments[0]));
            LidGuardCommandConsole.TryWriteHelpForCommand(LidGuardPipeCommands.LiveStatus, out _);
            return 1;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArguments) =>
        {
            eventArguments.Cancel = true;
            cancellationTokenSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                var snapshot = await ReadFirstSnapshotAsync(cancellationTokenSource.Token);
                WriteSnapshotOnce(snapshot);
                return 0;
            }

            return await RunInteractiveAsync(cancellationTokenSource);
        }
        catch (OperationCanceledException) { return 0; }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<int> RunInteractiveAsync(CancellationTokenSource cancellationTokenSource)
    {
        var restoreAlternateScreen = TryEnterAlternateScreen();
        var restoreCursorVisibility = TrySetCursorVisibility(false, out var previousCursorVisibility);
        TryClearConsole();
        try
        {
            var renderState = new LiveStatusRenderState();
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                await using var snapshotEnumerator = new LidGuardRuntimeClient()
                    .SubscribeLiveStatusAsync(cancellationTokenSource.Token)
                    .GetAsyncEnumerator(cancellationTokenSource.Token);

                if (!await RenderLiveStatusSubscriptionAsync(snapshotEnumerator, cancellationTokenSource, renderState)) break;
                if (!await WaitForLiveStatusReconnectAsync(cancellationTokenSource, renderState)) break;
            }

            return 0;
        }
        finally
        {
            if (restoreCursorVisibility) TrySetCursorVisibility(previousCursorVisibility, out _);
            if (restoreAlternateScreen) TryExitAlternateScreen();
            else TryPreparePromptAfterInteractiveScreen();
        }
    }

    private static async Task<bool> RenderLiveStatusSubscriptionAsync(
        IAsyncEnumerator<LiveStatusSnapshot> snapshotEnumerator,
        CancellationTokenSource cancellationTokenSource,
        LiveStatusRenderState renderState)
    {
        while (!cancellationTokenSource.IsCancellationRequested)
        {
            if (!await TryReadNextSnapshotOrExitAsync(snapshotEnumerator, cancellationTokenSource, renderState)) return !cancellationTokenSource.IsCancellationRequested;
            RenderSnapshot(renderState, snapshotEnumerator.Current);
        }

        return false;
    }

    private static async Task<LiveStatusSnapshot> ReadFirstSnapshotAsync(CancellationToken cancellationToken)
    {
        await foreach (var snapshot in new LidGuardRuntimeClient().SubscribeLiveStatusAsync(cancellationToken))
        {
            return snapshot;
        }

        return LiveStatusSnapshot.Failure("The LidGuard runtime live-status stream ended before sending a snapshot.");
    }

    private static void WriteSnapshotOnce(LiveStatusSnapshot snapshot)
    {
        var screenLines = CreateScreenLines(snapshot, new LiveStatusTerminalSize(120, 40), enableStyles: false);
        foreach (var screenLine in screenLines) Console.WriteLine(screenLine.TrimEnd());
    }

    private static void RenderSnapshot(LiveStatusRenderState renderState, LiveStatusSnapshot snapshot)
    {
        var terminalSize = ReadTerminalSize();
        if (renderState.LastSnapshot is not null && terminalSize != renderState.LastTerminalSize) TryClearConsole();

        RenderSnapshot(snapshot, terminalSize, enableStyles: true);
        renderState.LastSnapshot = snapshot;
        renderState.LastTerminalSize = terminalSize;
    }

    private static void RenderSnapshot(LiveStatusSnapshot snapshot, LiveStatusTerminalSize terminalSize, bool enableStyles)
    {
        var screenLines = CreateScreenLines(snapshot, terminalSize, enableStyles);
        var screenText = string.Join(Environment.NewLine, screenLines);
        TrySetCursorPosition(0, 0);
        Console.Write(screenText);
    }

    private static IReadOnlyList<string> CreateScreenLines(LiveStatusSnapshot snapshot, LiveStatusTerminalSize terminalSize, bool enableStyles)
    {
        var screenWidth = Math.Max(10, terminalSize.Width - 1);
        var screenHeight = Math.Max(1, terminalSize.Height);
        var screenLines = new List<string>
        {
            FitLine(StyleStrong(Text("LiveStatusTitle", "LidGuard live-status"), enableStyles), screenWidth),
            FitLine(Text("LiveStatusExitHint", "Updates on runtime events and reconnects while unavailable. Press q, Escape, or Ctrl+C to exit."), screenWidth)
        };

        var remainingHeight = screenHeight - screenLines.Count;
        var runtimePanelHeight = Math.Min(7, Math.Max(0, remainingHeight));
        AppendPanel(screenLines, Text("LiveStatusRuntimePanelTitle", "Runtime"), CreateRuntimePanelLines(snapshot, enableStyles), runtimePanelHeight, screenWidth, enableStyles);

        remainingHeight = screenHeight - screenLines.Count;
        var sessionPanelHeight = Math.Min(Math.Max(4, snapshot.Response.Sessions.Length + 3), Math.Max(0, remainingHeight / 3));
        AppendPanel(screenLines, Text("LiveStatusSessionsPanelTitle", "Sessions"), CreateSessionPanelLines(snapshot.Response, enableStyles), sessionPanelHeight, screenWidth, enableStyles);

        remainingHeight = screenHeight - screenLines.Count;
        var hookPanelHeight = Math.Min(10, Math.Max(0, remainingHeight / 2));
        AppendPanel(screenLines, Text("LiveStatusHookPanelTitle", "Hook events"), CreateHookPanelLines(snapshot, enableStyles), hookPanelHeight, screenWidth, enableStyles);

        remainingHeight = screenHeight - screenLines.Count;
        AppendPanel(screenLines, Text("LiveStatusFlowPanelTitle", "Runtime flow"), CreateFlowPanelLines(snapshot, enableStyles), remainingHeight, screenWidth, enableStyles);

        while (screenLines.Count < screenHeight) screenLines.Add(new string(' ', screenWidth));
        if (screenLines.Count > screenHeight) screenLines = screenLines.Take(screenHeight).ToList();
        return screenLines;
    }

    private static IReadOnlyList<string> CreateRuntimePanelLines(LiveStatusSnapshot snapshot, bool enableStyles)
    {
        var response = snapshot.Response;
        var runtimeState = CreateRuntimeStateText(response, enableStyles);
        var responseMessage = LidGuardRuntimeResponseLocalizer.Localize(response);
        var pendingSuspend = response.SuspendScheduled
            ? StyleWarning(CreatePendingSuspendText(response), enableStyles)
            : StyleMuted(Text("LiveStatusNone", "none"), enableStyles);

        return
        [
            Format("LiveStatusRuntimeStateLine", "State: {0} | Last update: {1}", runtimeState, StyleMuted(FormatCompactTimestamp(snapshot.UpdatedAt), enableStyles)),
            Format(
                "LiveStatusRuntimeCountsLine",
                "Active sessions: {0} | Lid: {1} | Visible monitors: {2}",
                CreateActiveSessionCountText(response.ActiveSessionCount, enableStyles),
                DisplayLidSwitchState(response, enableStyles),
                CreateVisibleDisplayMonitorCountText(response, enableStyles)),
            Format("LiveStatusPendingSuspendLine", "Pending suspend: {0}", pendingSuspend),
            string.IsNullOrWhiteSpace(responseMessage) ? Text("LiveStatusNoRuntimeMessage", "Runtime message: none") : Format("LiveStatusRuntimeMessageLine", "Runtime message: {0}", responseMessage)
        ];
    }

    private static string CreateRuntimeStateText(LidGuardPipeResponse response, bool enableStyles)
    {
        if (response.Succeeded) return StyleSuccess(Text("LiveStatusRunning", "running"), enableStyles);
        if (response.RuntimeUnavailable) return StyleFailure(Text("LiveStatusNotRunning", "not running"), enableStyles);

        return StyleFailure(Text("LiveStatusError", "error"), enableStyles);
    }

    private static string CreateActiveSessionCountText(int activeSessionCount, bool enableStyles)
    {
        var activeSessionCountText = activeSessionCount.ToString(CultureInfo.InvariantCulture);
        return activeSessionCount > 0 ? StyleWarning(activeSessionCountText, enableStyles) : StyleMuted(activeSessionCountText, enableStyles);
    }

    private static string CreateVisibleDisplayMonitorCountText(LidGuardPipeResponse response, bool enableStyles)
    {
        var visibleDisplayMonitorCountText = DisplayVisibleDisplayMonitorCount(response);
        if (response.RuntimeUnavailable) return StyleMuted(visibleDisplayMonitorCountText, enableStyles);

        return StyleStrong(visibleDisplayMonitorCountText, enableStyles);
    }

    private static IReadOnlyList<string> CreateSessionPanelLines(LidGuardPipeResponse response, bool enableStyles)
    {
        if (response.Sessions.Length == 0) return [StyleMuted(Text("LiveStatusNoSessions", "No active sessions."), enableStyles)];

        var sessionLines = new List<string>();
        foreach (var session in response.Sessions.OrderBy(static session => session.Provider).ThenBy(static session => session.SessionIdentifier, StringComparer.OrdinalIgnoreCase))
        {
            var providerDisplayText = StyleStrong(AgentProviderDisplay.CreateProviderDisplayText(session.Provider, session.ProviderName), enableStyles);
            var processText = session.WatchedProcessIdentifier > 0 ? session.WatchedProcessIdentifier.ToString(CultureInfo.InvariantCulture) : LidGuardText.SessionProcessNone;
            sessionLines.Add(Format(
                "LiveStatusSessionLine",
                "{0} session={1} process={2} softLock={3} started={4} last={5} workingDirectory={6}",
                providerDisplayText,
                StyleCyan(DisplayValue(session.SessionIdentifier), enableStyles),
                processText,
                DescribeSoftLockStatus(session, enableStyles),
                FormatCompactTimestamp(session.StartedAt),
                FormatCompactTimestamp(session.LastActivityAt),
                DisplayValue(session.WorkingDirectory)));
        }

        return sessionLines;
    }

    private static IReadOnlyList<string> CreateHookPanelLines(LiveStatusSnapshot snapshot, bool enableStyles)
    {
        if (snapshot.HookEventLines.Length == 0) return [StyleMuted(Text("LiveStatusNoHookEvents", "No recent received/runtime-result hook events."), enableStyles)];

        return snapshot.HookEventLines
            .Take(HookEventDisplayLineCount)
            .Select(hookEventLine => $"{StyleStrong(hookEventLine.ProviderDisplayText, enableStyles)} {LidGuardCommandTimestampFormatter.FormatHookEventLineForDisplay(hookEventLine.Line)}")
            .ToArray();
    }

    private static IReadOnlyList<string> CreateFlowPanelLines(LiveStatusSnapshot snapshot, bool enableStyles)
    {
        var flowEventLines = new List<LiveStatusFlowEventLine>();
        foreach (var warningMessage in snapshot.WarningMessages) flowEventLines.Add(new LiveStatusFlowEventLine(DateTimeOffset.MaxValue, StyleWarning(LidGuardText.TextWarning(warningMessage), enableStyles)));

        foreach (var runtimeLogEntry in snapshot.RuntimeLogEntries)
        {
            if (!IsRuntimeFlowEvent(runtimeLogEntry)) continue;
            flowEventLines.Add(new LiveStatusFlowEventLine(runtimeLogEntry.Timestamp, CreateRuntimeLogLine(runtimeLogEntry, enableStyles)));
        }

        foreach (var suspendHistoryEntry in snapshot.SuspendHistoryEntries) flowEventLines.Add(new LiveStatusFlowEventLine(suspendHistoryEntry.RecordedAt, CreateSuspendHistoryLine(suspendHistoryEntry, enableStyles)));

        var displayLines = flowEventLines
            .OrderByDescending(static flowEventLine => flowEventLine.Timestamp)
            .Take(FlowEventDisplayLineCount)
            .Select(static flowEventLine => flowEventLine.Line)
            .ToArray();
        return displayLines.Length == 0 ? [StyleMuted(Text("LiveStatusNoFlowEvents", "No recent runtime flow events."), enableStyles)] : displayLines;
    }

    private static bool IsRuntimeFlowEvent(LidGuardRuntimeSessionLogEntry runtimeLogEntry)
    {
        var eventName = runtimeLogEntry.EventName ?? string.Empty;
        return eventName.Contains("webhook", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("sound", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("suspend", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("session", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("emergency-hibernation", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRuntimeLogLine(LidGuardRuntimeSessionLogEntry runtimeLogEntry, bool enableStyles)
    {
        var lineBuilder = new StringBuilder();
        lineBuilder.Append(StyleMuted(FormatCompactTimestamp(runtimeLogEntry.Timestamp), enableStyles));
        lineBuilder.Append(' ');
        lineBuilder.Append(StyleCyan(Text("LiveStatusRuntimeFlowPrefix", "runtime"), enableStyles));
        lineBuilder.Append(" event=");
        lineBuilder.Append(DisplayValue(runtimeLogEntry.EventName));
        lineBuilder.Append(" command=");
        lineBuilder.Append(DisplayValue(runtimeLogEntry.Command));
        lineBuilder.Append(' ');
        lineBuilder.Append(Text("LiveStatusSucceededLabel", "succeeded"));
        lineBuilder.Append('=');
        lineBuilder.Append(CreateSucceededText(runtimeLogEntry.Succeeded, enableStyles));
        lineBuilder.Append(' ');
        lineBuilder.Append(Text("LiveStatusActiveSessionsShortLabel", "active"));
        lineBuilder.Append('=');
        lineBuilder.Append(runtimeLogEntry.ActiveSessionCount.ToString(CultureInfo.InvariantCulture));

        var sessionText = CreateSessionReferenceText(runtimeLogEntry.Provider, runtimeLogEntry.ProviderName, runtimeLogEntry.SessionIdentifier);
        if (!string.IsNullOrWhiteSpace(sessionText))
        {
            lineBuilder.Append(' ');
            lineBuilder.Append(Text("LiveStatusSessionShortLabel", "session"));
            lineBuilder.Append('=');
            lineBuilder.Append(sessionText);
        }

        if (!string.IsNullOrWhiteSpace(runtimeLogEntry.Message))
        {
            lineBuilder.Append(' ');
            lineBuilder.Append(Text("LiveStatusMessageShortLabel", "message"));
            lineBuilder.Append('=');
            lineBuilder.Append(runtimeLogEntry.Message);
        }

        return lineBuilder.ToString();
    }

    private static string CreateSuspendHistoryLine(SuspendHistoryEntry suspendHistoryEntry, bool enableStyles)
    {
        var lineBuilder = new StringBuilder();
        lineBuilder.Append(StyleMuted(FormatCompactTimestamp(suspendHistoryEntry.RecordedAt), enableStyles));
        lineBuilder.Append(' ');
        lineBuilder.Append(StyleCyan(Text("LiveStatusSuspendHistoryPrefix", "history"), enableStyles));
        lineBuilder.Append(" event=");
        lineBuilder.Append(DisplayValue(suspendHistoryEntry.EventName));
        lineBuilder.Append(" mode=");
        lineBuilder.Append(LidGuardText.DisplaySuspendMode(suspendHistoryEntry.SuspendMode));
        lineBuilder.Append(" reason=");
        lineBuilder.Append(DisplaySuspendWebhookReason(suspendHistoryEntry.Reason));
        lineBuilder.Append(' ');
        lineBuilder.Append(Text("LiveStatusSucceededLabel", "succeeded"));
        lineBuilder.Append('=');
        lineBuilder.Append(CreateSucceededText(suspendHistoryEntry.Succeeded, enableStyles));
        lineBuilder.Append(' ');
        lineBuilder.Append(Text("LiveStatusActiveSessionsShortLabel", "active"));
        lineBuilder.Append('=');
        lineBuilder.Append(suspendHistoryEntry.ActiveSessionCount.ToString(CultureInfo.InvariantCulture));

        var sessionText = CreateSessionReferenceText(suspendHistoryEntry.Provider, suspendHistoryEntry.ProviderName, suspendHistoryEntry.SessionIdentifier);
        if (!string.IsNullOrWhiteSpace(sessionText))
        {
            lineBuilder.Append(' ');
            lineBuilder.Append(Text("LiveStatusSessionShortLabel", "session"));
            lineBuilder.Append('=');
            lineBuilder.Append(sessionText);
        }

        if (suspendHistoryEntry.ObservedTemperatureCelsius is not null)
        {
            lineBuilder.Append(" temperature=");
            lineBuilder.Append(suspendHistoryEntry.ObservedTemperatureCelsius.Value.ToString(CultureInfo.InvariantCulture));
            lineBuilder.Append("C");
        }

        if (!string.IsNullOrWhiteSpace(suspendHistoryEntry.Message))
        {
            lineBuilder.Append(' ');
            lineBuilder.Append(Text("LiveStatusMessageShortLabel", "message"));
            lineBuilder.Append('=');
            lineBuilder.Append(suspendHistoryEntry.Message);
        }

        return lineBuilder.ToString();
    }

    private static string CreateSessionReferenceText(AgentProvider provider, string providerName, string sessionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return string.Empty;

        var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(provider, providerName);
        return $"{providerDisplayText}:{sessionIdentifier}";
    }

    private static string CreatePendingSuspendText(LidGuardPipeResponse response)
    {
        var suspendDelayText = response.SuspendDelaySeconds == 0
            ? Text("LiveStatusImmediate", "immediate")
            : Format("LiveStatusDelaySeconds", "{0} second(s)", response.SuspendDelaySeconds);
        return Format(
            "LiveStatusPendingSuspendDetails",
            "{0}, delay={1}, reason={2}",
            LidGuardText.DisplaySuspendMode(response.SuspendMode),
            suspendDelayText,
            DisplaySuspendReason(response.SuspendReasonCode));
    }

    private static string DisplaySuspendReason(string suspendReasonCode)
    {
        if (suspendReasonCode == LidGuardPipeResponseMessageCodes.SuspendReasonSoftLocked) return Text("LiveStatusSuspendReasonSoftLocked", "soft-locked");
        if (suspendReasonCode == LidGuardPipeResponseMessageCodes.SuspendReasonCompleted) return Text("LiveStatusSuspendReasonCompleted", "completed");

        return DisplayValue(suspendReasonCode);
    }

    private static string DisplaySuspendWebhookReason(SuspendWebhookReason reason)
        => LidGuardText.GetResourceString($"DisplaySuspendWebhookReason{reason}", reason.ToString());

    private static string DisplayLidSwitchState(LidGuardPipeResponse response, bool enableStyles)
    {
        if (response.RuntimeUnavailable) return StyleMuted(LidGuardText.TextDisplayNone, enableStyles);

        var lidSwitchStateText = LidGuardText.DisplayLidSwitchState(response.LidSwitchState);
        return response.LidSwitchState switch
        {
            LidSwitchState.Open => StyleSuccess(lidSwitchStateText, enableStyles),
            LidSwitchState.Closed => StyleFailure(lidSwitchStateText, enableStyles),
            _ => StyleWarning(lidSwitchStateText, enableStyles)
        };
    }

    private static string DisplayVisibleDisplayMonitorCount(LidGuardPipeResponse response)
        => response.RuntimeUnavailable ? LidGuardText.TextDisplayNone : response.VisibleDisplayMonitorCount.ToString(CultureInfo.InvariantCulture);

    private static string DescribeSoftLockStatus(LidGuardSessionStatus session, bool enableStyles)
    {
        if (session.SoftLockState != LidGuardSessionSoftLockState.SoftLocked) return StyleSuccess(LidGuardText.DisplaySessionSoftLockState(session.SoftLockState), enableStyles);

        var details = LidGuardText.DisplaySessionSoftLockState(session.SoftLockState);
        if (!string.IsNullOrWhiteSpace(session.SoftLockReason)) details = $"{details}:{session.SoftLockReason}";
        if (session.SoftLockedAt is not null) details = $"{details}@{FormatCompactTimestamp(session.SoftLockedAt.Value)}";
        return StyleFailure(details, enableStyles);
    }

    private static string FormatCompactTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp == DateTimeOffset.MinValue) return LidGuardText.TextDisplayNone;
        if (timestamp == DateTimeOffset.MaxValue) return LidGuardText.TextDisplayNone;

        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    }

    private static void AppendPanel(
        List<string> screenLines,
        string title,
        IReadOnlyList<string> contentLines,
        int maximumHeight,
        int screenWidth,
        bool enableStyles)
    {
        if (maximumHeight <= 0) return;

        screenLines.Add(FitLine(CreatePanelBorder(title, screenWidth, enableStyles), screenWidth));
        if (maximumHeight == 1) return;

        var contentCapacity = Math.Max(0, maximumHeight - 2);
        var visibleContentLines = contentLines.Take(contentCapacity).ToList();
        if (contentLines.Count > contentCapacity && contentCapacity > 0) visibleContentLines[^1] = Format("LiveStatusMoreEntries", "... {0} more", contentLines.Count - contentCapacity + 1);

        foreach (var contentLine in visibleContentLines) screenLines.Add(CreatePanelContentLine(contentLine, screenWidth));
        if (maximumHeight > 1) screenLines.Add(FitLine(CreatePanelBorder(string.Empty, screenWidth, enableStyles), screenWidth));
    }

    private static string CreatePanelBorder(string title, int screenWidth, bool enableStyles)
    {
        if (screenWidth <= 1) return StyleMuted(new string('-', Math.Max(0, screenWidth)), enableStyles);
        if (string.IsNullOrWhiteSpace(title)) return StyleMuted($"+{new string('-', Math.Max(0, screenWidth - 2))}+", enableStyles);

        var titleText = $" {StyleStrong(title, enableStyles)} ";
        var remainingWidth = Math.Max(0, screenWidth - GetDisplayWidth(titleText) - 2);
        return StyleMuted($"+{titleText}{new string('-', remainingWidth)}+", enableStyles);
    }

    private static string CreatePanelContentLine(string contentLine, int screenWidth)
    {
        var contentWidth = Math.Max(0, screenWidth - 4);
        var fittedContent = FitLine(contentLine, contentWidth);
        return $"| {fittedContent} |";
    }

    private static string FitLine(string line, int screenWidth)
    {
        var fittedLine = Truncate(line, screenWidth);
        return fittedLine + new string(' ', Math.Max(0, screenWidth - GetDisplayWidth(fittedLine)));
    }

    private static string Truncate(string value, int maximumWidth)
    {
        value ??= string.Empty;
        if (maximumWidth <= 0) return string.Empty;
        if (GetDisplayWidth(value) <= maximumWidth) return value;
        if (maximumWidth <= 3) return TakeDisplayWidth(value, maximumWidth);

        return TakeDisplayWidth(value, maximumWidth - 3) + "...";
    }

    private static string TakeDisplayWidth(string value, int maximumWidth)
    {
        if (maximumWidth <= 0) return string.Empty;

        var lineBuilder = new StringBuilder();
        var displayWidth = 0;
        var copiedTerminalStyle = false;
        var truncatedLine = false;
        for (var characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            if (TryReadTerminalStyleSequence(value, ref characterIndex, out var terminalStyleSequence))
            {
                lineBuilder.Append(terminalStyleSequence);
                copiedTerminalStyle = true;
                continue;
            }

            var codePoint = GetCodePoint(value, ref characterIndex, out var unicodeCategory);
            var characterWidth = GetCodePointDisplayWidth(codePoint, unicodeCategory);
            if (displayWidth + characterWidth > maximumWidth)
            {
                truncatedLine = true;
                break;
            }

            AppendCodePoint(lineBuilder, codePoint);
            displayWidth += characterWidth;
        }

        if (copiedTerminalStyle && truncatedLine) lineBuilder.Append(StyleResetSequence);
        return lineBuilder.ToString();
    }

    private static void AppendCodePoint(StringBuilder lineBuilder, int codePoint)
    {
        if (codePoint > char.MaxValue) lineBuilder.Append(char.ConvertFromUtf32(codePoint));
        else lineBuilder.Append((char)codePoint);
    }

    private static int GetDisplayWidth(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        var displayWidth = 0;
        for (var characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            if (TryReadTerminalStyleSequence(value, ref characterIndex, out _)) continue;

            var codePoint = GetCodePoint(value, ref characterIndex, out var unicodeCategory);
            displayWidth += GetCodePointDisplayWidth(codePoint, unicodeCategory);
        }

        return displayWidth;
    }

    private static int GetCodePoint(string value, ref int characterIndex, out UnicodeCategory unicodeCategory)
    {
        var character = value[characterIndex];
        unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(value, characterIndex);
        if (!char.IsHighSurrogate(character) || characterIndex + 1 >= value.Length || !char.IsLowSurrogate(value[characterIndex + 1])) return character;

        characterIndex++;
        return char.ConvertToUtf32(character, value[characterIndex]);
    }

    private static int GetCodePointDisplayWidth(int codePoint, UnicodeCategory unicodeCategory)
    {
        if (unicodeCategory is UnicodeCategory.Control or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;

        return IsWideCodePoint(codePoint) ? 2 : 1;
    }

    private static bool IsWideCodePoint(int codePoint)
        => codePoint is >= 0x1100 and <= 0x115F
            or >= 0x2329 and <= 0x232A
            or >= 0x2E80 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE10 and <= 0xFE19
            or >= 0xFE30 and <= 0xFE6F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6;

    private static bool TryReadTerminalStyleSequence(string value, ref int characterIndex, out string terminalStyleSequence)
    {
        terminalStyleSequence = string.Empty;
        if (value[characterIndex] != '\u001b' || characterIndex + 1 >= value.Length || value[characterIndex + 1] != '[') return false;

        for (var sequenceIndex = characterIndex + 2; sequenceIndex < value.Length; sequenceIndex++)
        {
            var sequenceCharacter = value[sequenceIndex];
            if (sequenceCharacter is < '\u0040' or > '\u007e') continue;

            terminalStyleSequence = value[characterIndex..(sequenceIndex + 1)];
            characterIndex = sequenceIndex;
            return true;
        }

        return false;
    }

    private static async Task<bool> TryReadNextSnapshotOrExitAsync(
        IAsyncEnumerator<LiveStatusSnapshot> snapshotEnumerator,
        CancellationTokenSource cancellationTokenSource,
        LiveStatusRenderState renderState)
    {
        var moveNextTask = snapshotEnumerator.MoveNextAsync().AsTask();
        while (!moveNextTask.IsCompleted)
        {
            if (cancellationTokenSource.Token.IsCancellationRequested)
            {
                await WaitForLiveStatusMoveNextCompletionAsync(moveNextTask);
                return false;
            }

            if (TryReadExitKey())
            {
                cancellationTokenSource.Cancel();
                await WaitForLiveStatusMoveNextCompletionAsync(moveNextTask);
                return false;
            }

            TryRenderLatestSnapshotAfterTerminalResize(renderState);
            await Task.WhenAny(moveNextTask, Task.Delay(s_keyPollInterval));
        }

        return await moveNextTask;
    }

    private static async Task WaitForLiveStatusMoveNextCompletionAsync(Task<bool> moveNextTask)
    {
        try { await moveNextTask; }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (NotSupportedException) { }
    }

    private static async Task<bool> WaitForLiveStatusReconnectAsync(
        CancellationTokenSource cancellationTokenSource,
        LiveStatusRenderState renderState)
    {
        var reconnectAt = DateTimeOffset.UtcNow.Add(s_runtimeReconnectInterval);
        while (!cancellationTokenSource.IsCancellationRequested && DateTimeOffset.UtcNow < reconnectAt)
        {
            if (TryReadExitKey())
            {
                cancellationTokenSource.Cancel();
                return false;
            }

            TryRenderLatestSnapshotAfterTerminalResize(renderState);
            var remainingDelay = reconnectAt - DateTimeOffset.UtcNow;
            var delay = remainingDelay < s_keyPollInterval ? remainingDelay : s_keyPollInterval;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationTokenSource.Token);
        }

        return !cancellationTokenSource.IsCancellationRequested;
    }

    private static void TryRenderLatestSnapshotAfterTerminalResize(LiveStatusRenderState renderState)
    {
        if (renderState.LastSnapshot is null) return;

        var terminalSize = ReadTerminalSize();
        if (terminalSize == renderState.LastTerminalSize) return;

        TryClearConsole();
        RenderSnapshot(renderState.LastSnapshot, terminalSize, enableStyles: true);
        renderState.LastTerminalSize = terminalSize;
    }

    private static bool TryReadExitKey()
    {
        try
        {
            while (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key is ConsoleKey.Q or ConsoleKey.Escape) return true;
            }
        }
        catch (InvalidOperationException) { }
        catch (IOException) { }

        return false;
    }

    private static LiveStatusTerminalSize ReadTerminalSize()
    {
        try { return new LiveStatusTerminalSize(Console.WindowWidth, Console.WindowHeight); }
        catch (IOException) { return new LiveStatusTerminalSize(120, 40); }
        catch (PlatformNotSupportedException) { return new LiveStatusTerminalSize(120, 40); }
    }

    private static void TryClearConsole()
    {
        try { Console.Clear(); }
        catch (IOException) { }
    }

    private static void TrySetCursorPosition(int left, int top)
    {
        try { Console.SetCursorPosition(left, top); }
        catch (IOException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private static bool TrySetCursorVisibility(bool visible, out bool previousCursorVisibility)
    {
        previousCursorVisibility = true;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            previousCursorVisibility = Console.CursorVisible;
            Console.CursorVisible = visible;
            return true;
        }
        catch (IOException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }

    private static bool TryEnterAlternateScreen()
    {
        if (IsDumbTerminal()) return false;

        try
        {
            Console.Write(EnterAlternateScreenSequence);
            Console.Out.Flush();
            return true;
        }
        catch (IOException) { return false; }
    }

    private static void TryExitAlternateScreen()
    {
        try
        {
            Console.Write(ExitAlternateScreenSequence);
            Console.Out.Flush();
        }
        catch (IOException) { }
    }

    private static void TryPreparePromptAfterInteractiveScreen()
    {
        try
        {
            var terminalSize = ReadTerminalSize();
            var promptLineTop = Math.Max(0, terminalSize.Height - 1);
            Console.SetCursorPosition(0, promptLineTop);
            Console.Write(new string(' ', Math.Max(0, terminalSize.Width - 1)));
            Console.SetCursorPosition(0, promptLineTop);
        }
        catch (IOException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private static bool IsDumbTerminal()
    {
        var terminalName = Environment.GetEnvironmentVariable("TERM");
        return !string.IsNullOrWhiteSpace(terminalName) && terminalName.Equals("dumb", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSucceededText(bool succeeded, bool enableStyles)
        => succeeded ? StyleSuccess(LidGuardText.DisplayBoolean(true), enableStyles) : StyleFailure(LidGuardText.DisplayBoolean(false), enableStyles);

    private static string StyleSuccess(string value, bool enableStyles)
        => StyleText(value, enableStyles, StyleBoldSequence, StyleGreenSequence);

    private static string StyleFailure(string value, bool enableStyles)
        => StyleText(value, enableStyles, StyleBoldSequence, StyleRedSequence);

    private static string StyleWarning(string value, bool enableStyles)
        => StyleText(value, enableStyles, StyleBoldSequence, StyleYellowSequence);

    private static string StyleStrong(string value, bool enableStyles)
        => StyleText(value, enableStyles, StyleBoldSequence);

    private static string StyleMuted(string value, bool enableStyles)
        => StyleText(value, enableStyles, StyleDimSequence);

    private static string StyleCyan(string value, bool enableStyles)
        => StyleText(value, enableStyles, StyleCyanSequence);

    private static string StyleText(string value, bool enableStyles, params string[] styleSequences)
    {
        if (!enableStyles || string.IsNullOrEmpty(value) || styleSequences.Length == 0) return value;

        return string.Concat(styleSequences) + value + StyleResetSequence;
    }

    private static string DisplayValue(string value)
        => string.IsNullOrWhiteSpace(value) ? LidGuardText.TextDisplayNone : value.Trim();

    private static string Text(string resourceName, string fallbackValue)
        => LidGuardText.GetResourceString(resourceName, fallbackValue);

    private static string Format(string resourceName, string fallbackValue, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Text(resourceName, fallbackValue), arguments);

    private sealed record LiveStatusFlowEventLine(DateTimeOffset Timestamp, string Line);

    private sealed class LiveStatusRenderState
    {
        public LiveStatusSnapshot LastSnapshot { get; set; }

        public LiveStatusTerminalSize LastTerminalSize { get; set; }
    }

    private readonly record struct LiveStatusTerminalSize(int Width, int Height);
}

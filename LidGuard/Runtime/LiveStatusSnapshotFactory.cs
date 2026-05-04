using System.Globalization;
using LidGuard.Hooks;
using LidGuard.Ipc;
using LidGuard.Sessions;

namespace LidGuard.Runtime;

internal static class LiveStatusSnapshotFactory
{
    private const int HookEventReadLineCount = 30;
    private const int RuntimeLogEntryReadCount = 40;
    private const int SuspendHistoryEntryReadCount = 10;

    public static LiveStatusSnapshot Create(LidGuardPipeResponse response)
    {
        var warningMessages = new List<string>();
        if (!LidGuardRuntimeSessionLogStore.TryReadRecent(RuntimeLogEntryReadCount, out var runtimeLogEntries, out var runtimeLogMessage)) warningMessages.Add(runtimeLogMessage);
        if (!SuspendHistoryLogStore.TryReadRecent(SuspendHistoryEntryReadCount, out var suspendHistoryEntries, out var suspendHistoryMessage)) warningMessages.Add(suspendHistoryMessage);

        return new LiveStatusSnapshot
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Response = response,
            HookEventLines = ReadRecentHookEventLines(),
            RuntimeLogEntries = runtimeLogEntries,
            SuspendHistoryEntries = suspendHistoryEntries,
            WarningMessages = [.. warningMessages]
        };
    }

    private static LiveStatusHookEventLine[] ReadRecentHookEventLines()
    {
        var hookEventLines = new List<LiveStatusHookEventLine>();
        AddProviderHookEventLines(AgentProvider.Codex, CodexHookEventLog.ReadRecentLines(HookEventReadLineCount), hookEventLines);
        AddProviderHookEventLines(AgentProvider.Claude, ClaudeHookEventLog.ReadRecentLines(HookEventReadLineCount), hookEventLines);
        AddProviderHookEventLines(AgentProvider.GitHubCopilot, GitHubCopilotHookEventLog.ReadRecentLines(HookEventReadLineCount), hookEventLines);
        return [.. hookEventLines.OrderByDescending(static hookEventLine => hookEventLine.Timestamp)];
    }

    private static void AddProviderHookEventLines(
        AgentProvider provider,
        IReadOnlyList<string> eventLines,
        List<LiveStatusHookEventLine> hookEventLines)
    {
        var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(provider, string.Empty);
        foreach (var eventLine in eventLines)
        {
            if (!eventLine.Contains("kind=received", StringComparison.Ordinal) && !eventLine.Contains("kind=runtime-result", StringComparison.Ordinal)) continue;
            hookEventLines.Add(new LiveStatusHookEventLine
            {
                Timestamp = ParseLogLineTimestamp(eventLine),
                ProviderDisplayText = providerDisplayText,
                Line = eventLine
            });
        }
    }

    private static DateTimeOffset ParseLogLineTimestamp(string eventLine)
    {
        var separatorIndex = eventLine.IndexOf(' ', StringComparison.Ordinal);
        var timestampText = separatorIndex < 0 ? eventLine : eventLine[..separatorIndex];
        return DateTimeOffset.TryParseExact(timestampText, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
    }
}

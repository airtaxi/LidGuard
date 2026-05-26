using System.Diagnostics;
using System.Text.Json;
using LidGuard.Ipc;
using LidGuard.Sessions;
using LidGuard.Settings;

namespace LidGuard.Hooks;

internal abstract class HookCommandBase<THookInput>
    where THookInput : IHookCommandInput
{
    private const string ClosedLidPermissionRequestAskSoftLockReason = "closed_lid_permission_request_ask";

    protected abstract AgentProvider Provider { get; }

    protected async Task<HookCommandInputReadResult<THookInput>> ReadHookInputAsync(HookExecutionTiming timing, string emptyInputMessage, Func<string, HookCommandInputParseResult<THookInput>> parseHookInput, Func<string, string> createParseFailureMessage)
    {
        var inputReadStopwatch = Stopwatch.StartNew();
        var hookInputJson = await Console.In.ReadToEndAsync();
        inputReadStopwatch.Stop();
        timing.InputReadDuration = inputReadStopwatch.Elapsed;
        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            AppendMessage(emptyInputMessage);
            return HookCommandInputReadResult<THookInput>.Failure();
        }

        var parseStopwatch = Stopwatch.StartNew();
        var parseResult = parseHookInput(hookInputJson);
        parseStopwatch.Stop();
        timing.ParseDuration = parseStopwatch.Elapsed;
        if (parseResult.Succeeded) return HookCommandInputReadResult<THookInput>.Success(parseResult.HookInput);

        AppendMessage(createParseFailureMessage(parseResult.Message));
        return HookCommandInputReadResult<THookInput>.Failure();
    }

    protected async Task<int> SendRuntimeRequestAsync(string commandName, string hookEventName, THookInput hookInput, bool isProviderSessionEnd = false, string sessionEndReason = "", bool hasPendingProviderWork = false, string pendingProviderWorkReason = "", HookExecutionTiming timing = null)
    {
        var hasSettings = false;
        var settings = LidGuardSettings.Default;
        var settingsLoadStopwatch = Stopwatch.StartNew();
        if (commandName == LidGuardPipeCommands.Start)
        {
            if (!LidGuardSettingsStore.TryLoadOrCreate(out settings, out var settingsMessage))
            {
                settingsLoadStopwatch.Stop();
                if (timing is not null) timing.SettingsLoadDuration = settingsLoadStopwatch.Elapsed;
                AppendMessage(settingsMessage);
                return 0;
            }

            hasSettings = true;
        }

        settingsLoadStopwatch.Stop();
        if (timing is not null) timing.SettingsLoadDuration = settingsLoadStopwatch.Elapsed;

        var parentProcessResolveStopwatch = Stopwatch.StartNew();
        var watchedProcessIdentifier = HookCommandUtilities.ResolveHookWatchedProcessIdentifier(commandName, Provider, settings);
        parentProcessResolveStopwatch.Stop();
        if (timing is not null) timing.ParentProcessResolveDuration = parentProcessResolveStopwatch.Elapsed;

        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = Provider,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            IsProviderSessionEnd = isProviderSessionEnd,
            SessionEndReason = isProviderSessionEnd ? CreateSessionEndReason(hookEventName, hookInput, sessionEndReason) : string.Empty,
            HasPendingProviderWork = hasPendingProviderWork,
            PendingProviderWorkReason = pendingProviderWorkReason,
            WatchedProcessIdentifier = watchedProcessIdentifier,
            InputPrompt = commandName == LidGuardPipeCommands.Start ? hookInput.Prompt : string.Empty,
            WorkingDirectory = GetWorkingDirectory(hookInput),
            TranscriptPath = hookInput.TranscriptPath,
            CanReturnStopContinuation = commandName == LidGuardPipeCommands.Stop && CanReturnStopContinuation(hookEventName, hookInput),
            StopHookAlreadyActive = commandName == LidGuardPipeCommands.Stop && IsStopHookAlreadyActive(hookInput),
            LastAssistantMessage = commandName == LidGuardPipeCommands.Stop ? GetLastAssistantMessage(hookInput) : string.Empty,
            HasSettings = hasSettings,
            Settings = settings
        };

        var startRuntimeIfUnavailable = commandName == LidGuardPipeCommands.Start;
        var runtimeClientDiagnostics = new LidGuardRuntimeClientDiagnostics();
        var interprocessCommunicationStopwatch = Stopwatch.StartNew();
        var response = await new LidGuardRuntimeClient().SendAsync(request, startRuntimeIfUnavailable, diagnostics: runtimeClientDiagnostics);
        interprocessCommunicationStopwatch.Stop();
        if (timing is not null) timing.InterprocessCommunicationDuration = interprocessCommunicationStopwatch.Elapsed;

        AppendRuntimeResult(hookEventName, hookInput, commandName, response, timing?.CreateRuntimeResultDetails(runtimeClientDiagnostics) ?? string.Empty);
        if (response.StopContinuationRequested)
        {
            WriteStopContinuationDecision(response.StopContinuationPrompt);
            return 0;
        }

        if (commandName == LidGuardPipeCommands.Stop && !hasPendingProviderWork) ClearSessionState(hookInput);
        return 0;
    }

    protected async Task<int> SendSessionStateRequestAsync(string commandName, string hookEventName, THookInput hookInput, string sessionStateReason)
    {
        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = Provider,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            SessionStateReason = sessionStateReason,
            WorkingDirectory = GetWorkingDirectory(hookInput),
            TranscriptPath = hookInput.TranscriptPath
        };

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        AppendRuntimeResult(hookEventName, hookInput, commandName, response, string.Empty);
        return 0;
    }

    protected async Task<int> WriteClosedLidDecisionAsync(string hookEventName, THookInput hookInput, Func<LidGuardPipeResponse, string> createRuntimeUnavailableMessage, Func<LidGuardPipeResponse, string> createInactivePolicyMessage, Func<LidGuardPipeResponse, string> createDecisionMessage, Func<LidGuardPipeResponse, int> writeDecision, bool softLockWhenPermissionRequestAsk = false)
    {
        var response = await new LidGuardRuntimeClient().SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false);
        if (!response.Succeeded)
        {
            AppendMessage(createRuntimeUnavailableMessage(response));
            return 0;
        }

        if (!ClosedLidPolicyStatus.IsActive(response))
        {
            AppendMessage(createInactivePolicyMessage(response));
            return 0;
        }

        if (softLockWhenPermissionRequestAsk && response.Settings.ClosedLidPermissionRequestDecision == ClosedLidPermissionRequestDecision.Ask)
        {
            AppendMessage($"LidGuard {Provider} hook soft-locked closed-lid {hookEventName} because ClosedLidPermissionRequestDecision is Ask and left the provider permission flow unchanged.");
            await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionSoftLocked, hookEventName, hookInput, ClosedLidPermissionRequestAskSoftLockReason);
            return 0;
        }

        AppendMessage(createDecisionMessage(response));
        return writeDecision(response);
    }

    protected string GetSessionIdentifier(THookInput hookInput)
    {
        if (!string.IsNullOrWhiteSpace(hookInput.SessionIdentifier)) return hookInput.SessionIdentifier;

        var workingDirectory = GetWorkingDirectory(hookInput);
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        return $"{Provider}:{normalizedWorkingDirectory}";
    }

    protected string GetWorkingDirectory(THookInput hookInput) => string.IsNullOrWhiteSpace(hookInput.WorkingDirectory) ? Environment.CurrentDirectory : hookInput.WorkingDirectory;

    protected static string DescribeActivityReason(string hookEventName, string activityDetail)
    {
        if (string.IsNullOrWhiteSpace(activityDetail)) return hookEventName;
        return $"{hookEventName}:{activityDetail}";
    }

    protected virtual void ClearSessionState(THookInput hookInput)
    {
    }

    protected virtual bool CanReturnStopContinuation(string hookEventName, THookInput hookInput) => false;

    protected virtual string GetLastAssistantMessage(THookInput hookInput) => string.Empty;

    protected virtual bool IsStopHookAlreadyActive(THookInput hookInput) => false;

    protected abstract void AppendMessage(string message);

    protected abstract void AppendRuntimeResult(string hookEventName, THookInput hookInput, string commandName, LidGuardPipeResponse response, string details);

    protected abstract string CreateSessionEndReason(string hookEventName, THookInput hookInput, string sessionEndReason);

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory; }
    }

    private static void WriteStopContinuationDecision(string stopContinuationPrompt)
    {
        var output = new StopHookContinuationDecisionOutput
        {
            Reason = stopContinuationPrompt ?? string.Empty
        };
        Console.WriteLine(JsonSerializer.Serialize(output, LidGuardJsonSerializerContext.Default.StopHookContinuationDecisionOutput));
    }
}

internal readonly record struct HookCommandInputParseResult<THookInput>(bool Succeeded, THookInput HookInput, string Message)
{
    public static HookCommandInputParseResult<THookInput> Success(THookInput hookInput) => new(true, hookInput, string.Empty);

    public static HookCommandInputParseResult<THookInput> Failure(string message) => new(false, default, message ?? string.Empty);
}

internal readonly record struct HookCommandInputReadResult<THookInput>(bool Succeeded, THookInput HookInput)
{
    public static HookCommandInputReadResult<THookInput> Success(THookInput hookInput) => new(true, hookInput);

    public static HookCommandInputReadResult<THookInput> Failure() => new(false, default);
}

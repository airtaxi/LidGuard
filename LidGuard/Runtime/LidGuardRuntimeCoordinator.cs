using System.Diagnostics;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Power;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Sessions;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal sealed class LidGuardRuntimeCoordinator
{
    private const string SessionTimeoutCommandName = "session-timeout";
    private const string CodexTranscriptTurnAbortedCommandName = "codex-transcript-turn-aborted";
    private const string CodexTranscriptRequestUserInputPendingCommandName = "codex-transcript-request-user-input-pending";
    private const string ClaudeTranscriptInterruptedCommandName = "claude-transcript-interrupted";
    private const string GitHubCopilotTranscriptAbortCommandName = "github-copilot-transcript-abort";
    private const string ServerRuntimeCleanupCommandName = "server-runtime-cleanup";

    private static readonly TimeSpan s_processWatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_preSuspendWebhookTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_postSessionEndWebhookTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_emergencyHibernationWebhookTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_stopFollowUpWebhookTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_stopFollowUpPollInterval = TimeSpan.FromSeconds(1);
    private readonly IProcessExitWatcher _processExitWatcher;
    private readonly ConfiguredSoundPlaybackCoordinator _soundPlaybackCoordinator;
    private readonly ISystemSuspendService _systemSuspendService;
    private readonly ILidStateSource _lidStateSource;
    private readonly IVisibleDisplayMonitorCountProvider _visibleDisplayMonitorCountProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LidGuardSessionRegistry _sessionRegistry = new();
    private readonly Dictionary<LidGuardSessionKey, CancellationTokenSource> _watcherCancellationTokenSources = [];
    private readonly LidGuardProtectionCoordinator _protectionCoordinator;
    private readonly AgentTranscriptMonitor _codexTranscriptMonitor;
    private readonly AgentTranscriptMonitor _claudeTranscriptMonitor;
    private readonly AgentTranscriptMonitor _gitHubCopilotTranscriptMonitor;
    private readonly EmergencyHibernationThermalMonitor _emergencyHibernationThermalMonitor;
    private readonly Action _requestRuntimeStop;
    private readonly HashSet<CancellationTokenSource> _pendingSuspendCancellationTokenSourcesSuppressingPostSessionEndWebhook = [];

    private LidGuardSettings _settings;
    private CancellationTokenSource _pendingSuspendCancellationTokenSource;
    private CancellationTokenSource _sessionTimeoutCancellationTokenSource;
    private CancellationTokenSource _serverRuntimeCleanupCancellationTokenSource;
    private int _pendingPostSessionEndWebhookCount;
    private string _pendingStopFollowUpStatus = string.Empty;
    private bool _serverRuntimeStopRequested;

    public LiveStatusEventHub LiveStatusEvents { get; } = new();

    public LidGuardRuntimeCoordinator(LidGuardSettings initialSettings, IPowerRequestService powerRequestService, IProcessExitWatcher processExitWatcher, LidActionPolicyController lidActionPolicyController, ISystemSuspendService systemSuspendService, IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer, ISystemAudioVolumeController systemAudioVolumeController, ILidStateSource lidStateSource, IVisibleDisplayMonitorCountProvider visibleDisplayMonitorCountProvider, Action requestRuntimeStop = null)
    {
        _processExitWatcher = processExitWatcher;
        _soundPlaybackCoordinator = new ConfiguredSoundPlaybackCoordinator(postStopSuspendSoundPlayer, systemAudioVolumeController);
        _systemSuspendService = systemSuspendService;
        _lidStateSource = lidStateSource;
        _visibleDisplayMonitorCountProvider = visibleDisplayMonitorCountProvider;
        _protectionCoordinator = new LidGuardProtectionCoordinator(powerRequestService, lidActionPolicyController);
        _codexTranscriptMonitor = new AgentTranscriptMonitor(CreateCodexTranscriptMonitoringProfile(), HandleTranscriptActivityDetectedAsync, HandleTranscriptStopDetectedAsync, HandleTranscriptSoftLockDetectedAsync);
        _claudeTranscriptMonitor = new AgentTranscriptMonitor(CreateClaudeTranscriptMonitoringProfile(), HandleTranscriptActivityDetectedAsync, HandleTranscriptStopDetectedAsync, HandleTranscriptSoftLockDetectedAsync);
        _gitHubCopilotTranscriptMonitor = new AgentTranscriptMonitor(CreateGitHubCopilotTranscriptMonitoringProfile(), HandleTranscriptActivityDetectedAsync, HandleTranscriptStopDetectedAsync, HandleTranscriptSoftLockDetectedAsync);
        _emergencyHibernationThermalMonitor = new EmergencyHibernationThermalMonitor(CreateEmergencyHibernationThermalMonitorState, HandleEmergencyHibernationThresholdReachedAsync);
        _requestRuntimeStop = requestRuntimeStop;
        _settings = LidGuardSettings.Normalize(initialSettings);
    }

    public async Task<LidGuardPipeResponse> HandleAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = request.Command switch
        {
            LidGuardPipeCommands.Start => await StartAsync(request, cancellationToken),
            LidGuardPipeCommands.Stop => await StopAsync(request, cancellationToken),
            LidGuardPipeCommands.MarkSessionActive => await MarkSessionActiveAsync(request, cancellationToken),
            LidGuardPipeCommands.MarkSessionSoftLocked => await MarkSessionSoftLockedAsync(request, cancellationToken),
            LidGuardPipeCommands.RemoveSession => await RemoveSessionAsync(request, cancellationToken),
            LidGuardPipeCommands.Status => await GetStatusAsync(cancellationToken),
            LidGuardPipeCommands.CleanupOrphans => await CleanupOrphansAsync(cancellationToken),
            LidGuardPipeCommands.Settings => await UpdateSettingsAsync(request, cancellationToken),
            _ => LidGuardPipeResponse.Failure($"Unsupported command: {request.Command}", _sessionRegistry.ActiveSessionCount)
        };
        if (request.Command != LidGuardPipeCommands.Status) LiveStatusEvents.Signal();
        return response;
    }

    public async Task<LiveStatusSnapshot> CreateLiveStatusSnapshotAsync(CancellationToken cancellationToken)
    {
        LidGuardPipeResponse response;
        await _gate.WaitAsync(cancellationToken);
        try { response = CreateSuccessResponse("LidGuard runtime is running.", LidGuardPipeResponseMessageCodes.RuntimeIsRunning); }
        finally { _gate.Release(); }

        return LiveStatusSnapshotFactory.Create(response);
    }

    public async Task<bool> TryConsumeServerRuntimeStopRequestAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_serverRuntimeStopRequested) return false;

            _serverRuntimeStopRequested = false;
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<LidGuardPipeResponse> StartAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var response = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-start-rejected", request, response);
            return response;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (request.HasSettings)
            {
                var settingsResult = UpdateSettingsInsideGate(request.Settings);
                if (!settingsResult.Succeeded)
                {
                    var response = CreateFailureResponse(settingsResult);
                    LidGuardRuntimeLogWriter.AppendSessionLog("session-start-failed", request, response);
                    return response;
                }
            }

            var watchedProcessResolution = ResolveWatchedProcess(request);

            var transcriptMonitoringRegistrationResult = RegisterTranscriptMonitor(request);
            var startedAt = DateTimeOffset.UtcNow;
            var startRequest = new LidGuardSessionStartRequest
            {
                SessionIdentifier = request.SessionIdentifier,
                Provider = request.Provider,
                ProviderName = request.ProviderName,
                StartedAt = startedAt,
                LastActivityAt = startedAt,
                WatchedProcessIdentifier = watchedProcessResolution.ProcessIdentifier,
                WatchRegistrationKind = watchedProcessResolution.WatchRegistrationKind,
                InputPromptPreview = WebhookTextPreview.Create(request.InputPrompt),
                WorkingDirectory = request.WorkingDirectory,
                TranscriptPath = transcriptMonitoringRegistrationResult.ResolvedTranscriptPath
            };

            var snapshot = _sessionRegistry.StartOrUpdate(startRequest);
            CancelServerRuntimeCleanupInsideGate();
            var protectionResult = EnsureProtection();
            if (!protectionResult.Succeeded)
            {
                RemoveTranscriptMonitorSession(snapshot.Key);
                var stopRequest = new LidGuardSessionStopRequest
                {
                    SessionIdentifier = request.SessionIdentifier,
                    Provider = request.Provider,
                    ProviderName = request.ProviderName
                };
                _sessionRegistry.Stop(stopRequest, out _);
                var response = CreateFailureResponse(protectionResult);
                LidGuardRuntimeLogWriter.AppendSessionLog("session-start-failed", request, response);
                return response;
            }

            CancelPendingSuspend();
            StartWatcher(snapshot);
            ReconfigureSessionTimeoutMonitorInsideGate();
            AppendTranscriptMonitorRegistration(request, snapshot, transcriptMonitoringRegistrationResult);

            var watcherStatusKind = CreateWatcherStatusKind(snapshot);
            var watchMessage = CreateWatcherStatusMessage(watcherStatusKind, snapshot);
            var successResponse = CreateSuccessResponse($"Started {snapshot.Key}.{watchMessage}", LidGuardPipeResponseMessageCodes.SessionStarted, [snapshot.Key.ToString(), watcherStatusKind, snapshot.WatchedProcessIdentifier.ToString()]);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-started", request, successResponse, snapshot);
            return successResponse;
        }
        finally { _gate.Release(); }
    }

    private async Task<LidGuardPipeResponse> UpdateSettingsAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasSettings)
        {
            var response = LidGuardPipeResponse.Failure("Settings payload is required.", _sessionRegistry.ActiveSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog("settings-update-rejected", request, response);
            return response;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settingsResult = UpdateSettingsInsideGate(request.Settings);
            if (!settingsResult.Succeeded)
            {
                var response = CreateFailureResponse(settingsResult);
                LidGuardRuntimeLogWriter.AppendSessionLog("settings-update-failed", request, response);
                return response;
            }

            var successResponse = CreateSuccessResponse("Updated LidGuard runtime settings.", LidGuardPipeResponseMessageCodes.SettingsRuntimeUpdated);
            LidGuardRuntimeLogWriter.AppendSessionLog("settings-updated", request, successResponse);
            return successResponse;
        }
        finally { _gate.Release(); }
    }

    private async Task<LidGuardPipeResponse> StopAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        LidGuardPipeResponse response;
        StopFollowUpAwaitContext stopFollowUpAwaitContext;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stopRequest = new LidGuardSessionStopRequest
            {
                SessionIdentifier = request.SessionIdentifier,
                Provider = request.Provider,
                ProviderName = request.ProviderName,
                IsProviderSessionEnd = request.IsProviderSessionEnd,
                SessionEndReason = request.SessionEndReason,
                HasPendingProviderWork = request.HasPendingProviderWork,
                PendingProviderWorkReason = request.PendingProviderWorkReason,
                LastAssistantMessage = request.LastAssistantMessage
            };
            var sessionKey = new LidGuardSessionKey(stopRequest.Provider, stopRequest.SessionIdentifier, stopRequest.ProviderName);
            response = StopInsideGate(stopRequest, $"Stopped {sessionKey}.", request, out stopFollowUpAwaitContext, successMessageCode: LidGuardPipeResponseMessageCodes.SessionStopped, successMessageArguments: [sessionKey.ToString()]);
        }
        finally { _gate.Release(); }

        if (stopFollowUpAwaitContext is null) return response;
        return await AwaitStopFollowUpReplyAsync(stopFollowUpAwaitContext, cancellationToken);
    }

    private async Task<LidGuardPipeResponse> MarkSessionActiveAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return MarkSessionActiveInsideGate(request); }
        finally { _gate.Release(); }
    }

    private async Task<LidGuardPipeResponse> MarkSessionSoftLockedAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
            {
                var rejectedResponse = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
                LidGuardRuntimeLogWriter.AppendSessionLog("session-softlock-rejected", request, rejectedResponse);
                return rejectedResponse;
            }

            var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);
            return MarkSessionSoftLockedInsideGate(LidGuardPipeCommands.MarkSessionSoftLocked, "session-softlock-recorded", request.Provider, request.ProviderName, request.SessionIdentifier, request.SessionStateReason, key);
        }
        finally { _gate.Release(); }
    }

    private LidGuardPipeResponse MarkSessionSoftLockedInsideGate(string commandName, string eventName, AgentProvider provider, string providerName, string sessionIdentifier, string sessionStateReason, LidGuardSessionKey sessionKey)
    {
        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = provider,
            ProviderName = providerName,
            SessionIdentifier = sessionIdentifier,
            SessionStateReason = sessionStateReason
        };

        if (!_sessionRegistry.TryMarkSoftLocked(provider, sessionIdentifier, providerName, request.SessionStateReason, DateTimeOffset.UtcNow, out var snapshot, out var changed))
        {
            var ignoredResponse = CreateSuccessResponse($"Session {sessionKey} is not active; ignored soft-lock signal.");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-softlock-ignored", request, ignoredResponse);
            return ignoredResponse;
        }

        ResetTranscriptMonitorSession(sessionKey);
        var successMessage = changed ? $"Marked {sessionKey} as soft-locked from {request.SessionStateReason}." : $"Session {sessionKey} is already soft-locked from {snapshot.SoftLockReason}.";
        if (HasSessionsKeepingProtectionAppliedInsideGate())
        {
            var successResponse = CreateSuccessResponse(successMessage);
            LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, successResponse, snapshot);
            return successResponse;
        }

        var pendingSuspendContext = CreatePendingSuspendContext(request, snapshot);
        var successResponseWithSuspendPlan = HandleSuspendAfterProtectionRetainedOrReleased(pendingSuspendContext, snapshot, eventName, successMessage, string.Empty, null, _sessionRegistry.ActiveSessionCount, out _, out _);
        if (!successResponseWithSuspendPlan.Succeeded)
        {
            LidGuardRuntimeLogWriter.AppendSessionLog("session-softlock-failed", request, successResponseWithSuspendPlan, snapshot);
            return successResponseWithSuspendPlan;
        }

        LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, successResponseWithSuspendPlan, snapshot);
        return successResponseWithSuspendPlan;
    }

    private async Task<LidGuardPipeResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return CreateSuccessResponse("LidGuard runtime is running.", LidGuardPipeResponseMessageCodes.RuntimeIsRunning); }
        finally { _gate.Release(); }
    }

    private async Task<LidGuardPipeResponse> RemoveSessionAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (request.MatchAllSessions) return RemoveAllSessionsInsideGate(request);
            if (request.MatchAllProvidersForSessionIdentifier) return RemoveSessionsMatchingSessionIdentifierInsideGate(request);
            if (request.MatchAllProviderNamesForSessionIdentifier) return RemoveSessionsMatchingProviderInsideGate(request);

            var stopRequest = new LidGuardSessionStopRequest
            {
                SessionIdentifier = request.SessionIdentifier,
                Provider = request.Provider,
                ProviderName = request.ProviderName
            };
            var sessionKey = new LidGuardSessionKey(stopRequest.Provider, stopRequest.SessionIdentifier, stopRequest.ProviderName);
            return StopInsideGate(stopRequest, $"Removed {sessionKey}.", null, out _, "session-removed", LidGuardPipeCommands.RemoveSession, LidGuardPipeResponseMessageCodes.SessionRemoved, [sessionKey.ToString()]);
        }
        finally { _gate.Release(); }
    }

    private LidGuardPipeResponse RemoveAllSessionsInsideGate(LidGuardPipeRequest request)
    {
        var activeSnapshots = _sessionRegistry.GetSnapshots().ToArray();
        if (activeSnapshots.Length == 0)
        {
            var alreadyStoppedResponse = CreateSuccessResponse("There are no active sessions to remove.", LidGuardPipeResponseMessageCodes.SessionRemoveNoActiveSessions);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
            return alreadyStoppedResponse;
        }

        return RemoveSnapshotsInsideGate(request, activeSnapshots, $"Removed all {activeSnapshots.Length} active session(s).", LidGuardPipeResponseMessageCodes.SessionRemovedAll, [activeSnapshots.Length.ToString()]);
    }

    private async Task<LidGuardPipeResponse> CleanupOrphansAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var cleanupCount = 0;
            var cleanupFailureMessages = new List<string>();
            foreach (var snapshot in _sessionRegistry.GetSnapshots())
            {
                if (!snapshot.HasWatchedProcess) continue;
                if (IsProcessRunning(snapshot.WatchedProcessIdentifier)) continue;

                var cleanupResult = CleanupWatchedProcessExitInsideGate(snapshot, LidGuardPipeCommands.CleanupOrphans, "orphan-session-cleaned");
                var stopResponse = cleanupResult.Response;

                if (!stopResponse.Succeeded) cleanupFailureMessages.Add(stopResponse.Message);
                cleanupCount += cleanupResult.RemovedSessionCount;
            }

            if (cleanupFailureMessages.Count > 0)
            {
                var response = LidGuardPipeResponse.Failure(string.Join(" ", cleanupFailureMessages), _sessionRegistry.ActiveSessionCount);
                LidGuardRuntimeLogWriter.AppendRuntimeLog("cleanup-orphans-failed", LidGuardPipeCommands.CleanupOrphans, response);
                return response;
            }

            var successResponse = CreateSuccessResponse($"Cleaned {cleanupCount} orphan session(s).", LidGuardPipeResponseMessageCodes.CleanupOrphansCompleted, [cleanupCount.ToString()]);
            LidGuardRuntimeLogWriter.AppendRuntimeLog("cleanup-orphans-completed", LidGuardPipeCommands.CleanupOrphans, successResponse);
            return successResponse;
        }
        finally { _gate.Release(); }
    }

    private WatchedProcessResolution ResolveWatchedProcess(LidGuardPipeRequest request)
    {
        if (request.WatchedProcessIdentifier > 0) return new WatchedProcessResolution(request.WatchedProcessIdentifier, LidGuardSessionWatchRegistrationKind.ExplicitWatchedProcessIdentifier);

        if (request.Provider == AgentProvider.Mcp) return WatchedProcessResolution.None;
        if (!_settings.WatchParentProcess) return WatchedProcessResolution.None;
        if (!_sessionRegistry.TryGetSnapshot(request.Provider, request.SessionIdentifier, request.ProviderName, out var existingSnapshot)) return WatchedProcessResolution.None;
        if (!existingSnapshot.HasWatchedProcess) return WatchedProcessResolution.None;
        return new WatchedProcessResolution(existingSnapshot.WatchedProcessIdentifier, existingSnapshot.WatchRegistrationKind);
    }

    private static string CreateWatcherStatusMessage(string watcherStatusKind, LidGuardSessionSnapshot snapshot)
    {
        return watcherStatusKind switch
        {
            LidGuardPipeResponseMessageCodes.WatcherStatusWatchedProcess => $" Watching process {snapshot.WatchedProcessIdentifier}.",
            _ => " No watched process was resolved; a stop hook is required."
        };
    }

    private static string CreateWatcherStatusKind(LidGuardSessionSnapshot snapshot)
    {
        if (snapshot.HasWatchedProcess) return LidGuardPipeResponseMessageCodes.WatcherStatusWatchedProcess;
        return LidGuardPipeResponseMessageCodes.WatcherStatusNone;
    }

    private LidGuardOperationResult UpdateSettingsInsideGate(LidGuardSettings settings)
    {
        if (!LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(settings.PostStopSuspendSoundVolumeOverridePercent))
        {
            var message =
                $"Post-stop suspend sound volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.";
            return LidGuardOperationResult.Failure(message);
        }

        if (!LidGuardSettings.IsValidClosedLidStopFollowUpSoundVolumeOverridePercent(settings.ClosedLidStopFollowUpSoundVolumeOverridePercent))
        {
            var message =
                $"Closed-lid stop follow-up sound volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.";
            return LidGuardOperationResult.Failure(message);
        }

        if (!LidGuardSettings.IsValidSuspendHistoryEntryCount(settings.SuspendHistoryEntryCount))
        {
            var message = $"Suspend history count must be off or an integer of at least {LidGuardSettings.MinimumSuspendHistoryEntryCount}.";
            return LidGuardOperationResult.Failure(message);
        }

        if (!LidGuardSettings.IsValidSessionTimeoutMinutes(settings.SessionTimeoutMinutes))
        {
            var message = $"Session timeout minutes must be off or an integer of at least {LidGuardSettings.MinimumSessionTimeoutMinutes}.";
            return LidGuardOperationResult.Failure(message);
        }

        if (!LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes(settings.ServerRuntimeCleanupDelayMinutes))
        {
            var message =
                $"Server runtime cleanup delay minutes must be off to disable automatic runtime exit or an integer of at least {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}.";
            return LidGuardOperationResult.Failure(message);
        }

        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (LidGuardSettingsChangeDetector.AreEquivalent(_settings, normalizedSettings))
        {
            if (!_sessionRegistry.HasActiveSessions) return LidGuardOperationResult.Success();
            if (!HasSessionsKeepingProtectionAppliedInsideGate()) return LidGuardOperationResult.Success();
            if (_protectionCoordinator.IsApplied) return LidGuardOperationResult.Success();
        }

        if (!_sessionRegistry.HasActiveSessions)
        {
            _settings = normalizedSettings;
            ReconfigureWatchers();
            ReconfigureSessionTimeoutMonitorInsideGate();
            EnsureEmergencyHibernationThermalMonitor();
            ReconfigureServerRuntimeCleanupInsideGate();
            return LidGuardOperationResult.Success();
        }

        var previousSettings = _settings;
        CancelServerRuntimeCleanupInsideGate();
        var restoreResult = RestoreProtection();
        if (!restoreResult.Succeeded) return restoreResult;

        _settings = normalizedSettings;
        if (!HasSessionsKeepingProtectionAppliedInsideGate())
        {
            ReconfigureWatchers();
            ReconfigureSessionTimeoutMonitorInsideGate();
            EnsureEmergencyHibernationThermalMonitor();
            return LidGuardOperationResult.Success();
        }

        var protectionResult = EnsureProtection();
        if (protectionResult.Succeeded)
        {
            ReconfigureWatchers();
            ReconfigureSessionTimeoutMonitorInsideGate();
            EnsureEmergencyHibernationThermalMonitor();
            return LidGuardOperationResult.Success();
        }

        _settings = previousSettings;
        var rollbackResult = HasSessionsKeepingProtectionAppliedInsideGate() ? EnsureProtection() : LidGuardOperationResult.Success();
        if (!rollbackResult.Succeeded)
        {
            var message = $"{CreateResultMessage(protectionResult)} Rollback failed: {CreateResultMessage(rollbackResult)}";
            return LidGuardOperationResult.Failure(message);
        }

        ReconfigureWatchers();
        ReconfigureSessionTimeoutMonitorInsideGate();
        EnsureEmergencyHibernationThermalMonitor();
        return protectionResult;
    }

    private bool HasSessionsKeepingProtectionAppliedInsideGate()
    {
        foreach (var snapshot in _sessionRegistry.GetSnapshots())
        {
            if (snapshot.HasPendingProviderWork) return true;
            if (!snapshot.IsSoftLocked) return true;
        }

        return false;
    }

    private LidGuardOperationResult EnsureProtection()
    {
        var protectionResult = _protectionCoordinator.Ensure(_settings);
        if (!protectionResult.Succeeded) return protectionResult;

        EnsureEmergencyHibernationThermalMonitor();
        return LidGuardOperationResult.Success();
    }

    private LidGuardOperationResult RestoreProtection()
    {
        var restoreResult = _protectionCoordinator.Restore();
        CancelEmergencyHibernationThermalMonitor();
        return restoreResult;
    }

    private void LogSuspendProtectionRetainedInsideGate(string eventName, PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot)
    {
        if (!_protectionCoordinator.IsApplied) return;

        var response = CreateSuccessResponse("Retained LidGuard protection until the pending suspend request is ready.");
        LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-protection-retained", pendingSuspendContext, response, snapshot);
    }

    private LidGuardOperationResult ReleaseProtectionIfNoSessionRequiresItInsideGate(string eventName, PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string successMessage)
    {
        if (HasSessionsKeepingProtectionAppliedInsideGate()) return LidGuardOperationResult.Success();
        return ReleaseSuspendProtectionInsideGate(eventName, pendingSuspendContext, snapshot, successMessage);
    }

    private LidGuardOperationResult ReleaseSuspendProtectionInsideGate(string eventName, PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string successMessage)
    {
        if (!_protectionCoordinator.IsApplied) return LidGuardOperationResult.Success();

        var restoreResult = RestoreProtection();
        var response = restoreResult.Succeeded ? CreateSuccessResponse(successMessage) : CreateFailureResponse(restoreResult);
        LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-protection-released", pendingSuspendContext, response, snapshot);
        return restoreResult;
    }

    private void StartWatcher(LidGuardSessionSnapshot snapshot)
    {
        CancelWatcher(snapshot.Key);
        if (!_settings.WatchParentProcess) return;
        if (!snapshot.HasWatchedProcess) return;
        if (snapshot.HasPendingProviderWork) return;

        var cancellationTokenSource = new CancellationTokenSource();
        _watcherCancellationTokenSources[snapshot.Key] = cancellationTokenSource;
        _ = WatchProcessExitAsync(snapshot, cancellationTokenSource.Token);
    }

    private void ReconfigureWatchers()
    {
        foreach (var key in _watcherCancellationTokenSources.Keys.ToArray()) CancelWatcher(key);
        if (!_settings.WatchParentProcess) return;
        foreach (var snapshot in _sessionRegistry.GetSnapshots()) StartWatcher(snapshot);
    }

    private void ReconfigureSessionTimeoutMonitorInsideGate()
    {
        CancelSessionTimeoutMonitorInsideGate();
        if (!_sessionRegistry.HasActiveSessions) return;
        if (_settings.SessionTimeoutMinutes is not { } sessionTimeoutMinutes) return;

        var sessionTimeoutDuration = TimeSpan.FromMinutes(sessionTimeoutMinutes);
        var nextExpirationAt = DateTimeOffset.MaxValue;
        foreach (var snapshot in _sessionRegistry.GetSnapshots())
        {
            // Soft-locked sessions without pending work are already suspend-eligible and stay outside
            // the timer, but pending work must not exempt a session: a lost provider stop event would
            // otherwise keep protection alive with no timeout ever firing.
            if (snapshot.IsSoftLocked && !snapshot.HasPendingProviderWork) continue;

            var sessionExpirationAt = AddSessionTimeoutDuration(snapshot.LastActivityAt, sessionTimeoutDuration);
            if (sessionExpirationAt < nextExpirationAt) nextExpirationAt = sessionExpirationAt;
        }

        if (nextExpirationAt == DateTimeOffset.MaxValue) return;

        var delay = nextExpirationAt - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        var cancellationTokenSource = new CancellationTokenSource();
        _sessionTimeoutCancellationTokenSource = cancellationTokenSource;
        _ = WaitForSessionTimeoutAsync(delay, cancellationTokenSource);
    }

    private void CancelSessionTimeoutMonitorInsideGate()
    {
        if (_sessionTimeoutCancellationTokenSource is null) return;

        var cancellationTokenSource = _sessionTimeoutCancellationTokenSource;
        _sessionTimeoutCancellationTokenSource = null;
        cancellationTokenSource.Cancel();
    }

    private void ReconfigureServerRuntimeCleanupInsideGate(bool deferImmediateStopUntilCurrentResponse = true)
    {
        CancelServerRuntimeCleanupInsideGate();
        if (_sessionRegistry.HasActiveSessions) return;
        if (_pendingSuspendCancellationTokenSource is not null) return;
        if (_pendingPostSessionEndWebhookCount > 0) return;
        if (_settings.ServerRuntimeCleanupDelayMinutes is null) return;

        var serverRuntimeCleanupDelayMinutes = _settings.ServerRuntimeCleanupDelayMinutes.Value;
        if (serverRuntimeCleanupDelayMinutes == 0)
        {
            RequestServerRuntimeStopInsideGate(deferImmediateStopUntilCurrentResponse);
            return;
        }

        var delay = TimeSpan.FromMinutes(serverRuntimeCleanupDelayMinutes);
        var cancellationTokenSource = new CancellationTokenSource();
        _serverRuntimeCleanupCancellationTokenSource = cancellationTokenSource;
        LidGuardRuntimeLogWriter.AppendRuntimeLog("server-runtime-cleanup-scheduled", ServerRuntimeCleanupCommandName, CreateSuccessResponse($"Scheduled server runtime cleanup in {serverRuntimeCleanupDelayMinutes} minute(s)."));
        _ = StopServerRuntimeAfterCleanupDelayAsync(delay, cancellationTokenSource);
    }

    private void CancelServerRuntimeCleanupInsideGate()
    {
        _serverRuntimeStopRequested = false;
        if (_serverRuntimeCleanupCancellationTokenSource is null) return;

        var cancellationTokenSource = _serverRuntimeCleanupCancellationTokenSource;
        _serverRuntimeCleanupCancellationTokenSource = null;
        cancellationTokenSource.Cancel();
    }

    private async Task StopServerRuntimeAfterCleanupDelayAsync(TimeSpan delay, CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationTokenSource.Token);

            await _gate.WaitAsync(cancellationTokenSource.Token);
            try
            {
                if (!ReferenceEquals(_serverRuntimeCleanupCancellationTokenSource, cancellationTokenSource)) return;

                _serverRuntimeCleanupCancellationTokenSource = null;
                if (_sessionRegistry.HasActiveSessions) return;
                if (_pendingSuspendCancellationTokenSource is not null) return;

                RequestServerRuntimeStopInsideGate(false);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally { cancellationTokenSource.Dispose(); }
    }

    private void RequestServerRuntimeStopInsideGate(bool deferImmediateStopUntilCurrentResponse)
    {
        _serverRuntimeStopRequested = true;
        LidGuardRuntimeLogWriter.AppendRuntimeLog("server-runtime-cleanup-requested", ServerRuntimeCleanupCommandName, CreateSuccessResponse("Requested server runtime cleanup because no active sessions remain."));
        if (!deferImmediateStopUntilCurrentResponse) _requestRuntimeStop?.Invoke();
    }

    private async Task WaitForSessionTimeoutAsync(TimeSpan delay, CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationTokenSource.Token);

            await _gate.WaitAsync(cancellationTokenSource.Token);
            try
            {
                if (!ReferenceEquals(_sessionTimeoutCancellationTokenSource, cancellationTokenSource)) return;

                _sessionTimeoutCancellationTokenSource = null;
                HandleSessionTimeoutInsideGate();
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally { cancellationTokenSource.Dispose(); }
    }

    private void HandleSessionTimeoutInsideGate()
    {
        if (_settings.SessionTimeoutMinutes is not { } sessionTimeoutMinutes)
        {
            ReconfigureSessionTimeoutMonitorInsideGate();
            return;
        }

        var sessionTimeoutDuration = TimeSpan.FromMinutes(sessionTimeoutMinutes);
        var now = DateTimeOffset.UtcNow;
        var expiredSnapshots = _sessionRegistry
            .GetSnapshots()
            .Where(snapshot => !snapshot.IsSoftLocked || snapshot.HasPendingProviderWork)
            .Where(snapshot => now >= snapshot.LastActivityAt)
            .Where(snapshot => now - snapshot.LastActivityAt >= sessionTimeoutDuration)
            .ToArray();
        if (expiredSnapshots.Length == 0)
        {
            ReconfigureSessionTimeoutMonitorInsideGate();
            return;
        }

        foreach (var expiredSnapshot in expiredSnapshots)
        {
            if (expiredSnapshot.HasPendingProviderWork) AbandonPendingProviderWorkInsideGate(expiredSnapshot, sessionTimeoutMinutes);
            MarkSessionSoftLockedInsideGate(SessionTimeoutCommandName, "session-timeout-softlock-recorded", expiredSnapshot.Provider, expiredSnapshot.ProviderName, expiredSnapshot.SessionIdentifier, $"session-timeout-expired:{sessionTimeoutMinutes} minutes", expiredSnapshot.Key);
        }

        ReconfigureSessionTimeoutMonitorInsideGate();
    }

    private void AbandonPendingProviderWorkInsideGate(LidGuardSessionSnapshot snapshot, int sessionTimeoutMinutes)
    {
        if (!_sessionRegistry.TryClearPendingProviderWork(snapshot.Provider, snapshot.SessionIdentifier, snapshot.ProviderName, out var abandonedSnapshot)) return;

        var request = new LidGuardPipeRequest
        {
            Command = SessionTimeoutCommandName,
            Provider = snapshot.Provider,
            ProviderName = snapshot.ProviderName,
            SessionIdentifier = snapshot.SessionIdentifier,
            SessionStateReason = snapshot.PendingProviderWorkReason
        };
        var pendingProviderWorkReason = string.IsNullOrWhiteSpace(snapshot.PendingProviderWorkReason) ? "pending provider work remains" : snapshot.PendingProviderWorkReason.Trim();
        var response = CreateSuccessResponse($"Cleared pending provider work for {snapshot.Key} because '{pendingProviderWorkReason}' showed no provider activity for {sessionTimeoutMinutes} minute(s).");
        LidGuardRuntimeLogWriter.AppendSessionLog("session-pending-provider-work-abandoned", request, response, abandonedSnapshot);
        // Re-attach the process watcher so a still-running agent process still gets normal exit
        // cleanup even though its stop or completion event was lost.
        StartWatcher(abandonedSnapshot);
    }

    private static DateTimeOffset AddSessionTimeoutDuration(DateTimeOffset lastActivityAt, TimeSpan sessionTimeoutDuration)
    {
        try { return lastActivityAt + sessionTimeoutDuration; }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MaxValue; }
    }

    private void EnsureEmergencyHibernationThermalMonitor()
    {
        if (!_protectionCoordinator.IsApplied || !_settings.EmergencyHibernationOnHighTemperature)
        {
            CancelEmergencyHibernationThermalMonitor();
            return;
        }

        _emergencyHibernationThermalMonitor.EnsureStarted();
    }

    private void CancelEmergencyHibernationThermalMonitor() => _emergencyHibernationThermalMonitor.Cancel();

    private async Task WatchProcessExitAsync(LidGuardSessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var watchResult = await _processExitWatcher.WaitForExitAsync(snapshot.WatchedProcessIdentifier, s_processWatchInterval, cancellationToken);
            if (!watchResult.Succeeded) return;

            await _gate.WaitAsync(CancellationToken.None);
            try { CleanupWatchedProcessExitInsideGate(snapshot, LidGuardPipeCommands.Stop, "watched-process-exited"); }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
    }

    private LidGuardPipeResponse RemoveSessionsMatchingSessionIdentifierInsideGate(LidGuardPipeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var rejectedResponse = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-rejected", request, rejectedResponse);
            return rejectedResponse;
        }

        var matchingSnapshots = _sessionRegistry
            .GetSnapshots()
            .Where(snapshot => string.Equals(snapshot.SessionIdentifier, request.SessionIdentifier, StringComparison.Ordinal))
            .ToArray();
        if (matchingSnapshots.Length == 0)
        {
            var alreadyStoppedResponse = CreateSuccessResponse($"Session id {request.SessionIdentifier} is already stopped.", LidGuardPipeResponseMessageCodes.SessionIdAlreadyStopped, [request.SessionIdentifier]);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
            return alreadyStoppedResponse;
        }

        return RemoveSnapshotsInsideGate(request, matchingSnapshots, $"Removed {matchingSnapshots.Length} session(s) matching session id \"{request.SessionIdentifier}\".", LidGuardPipeResponseMessageCodes.SessionRemovedMatchingSessionId, [matchingSnapshots.Length.ToString(), request.SessionIdentifier]);
    }

    private LidGuardPipeResponse RemoveSessionsMatchingProviderInsideGate(LidGuardPipeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var rejectedResponse = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-rejected", request, rejectedResponse);
            return rejectedResponse;
        }

        var matchingSnapshots = _sessionRegistry
            .GetSnapshots()
            .Where(snapshot => snapshot.Provider == request.Provider)
            .Where(snapshot => string.Equals(snapshot.SessionIdentifier, request.SessionIdentifier, StringComparison.Ordinal))
            .ToArray();
        if (matchingSnapshots.Length == 0)
        {
            var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(request.Provider, request.ProviderName);
            var alreadyStoppedResponse = CreateSuccessResponse($"Session id {request.SessionIdentifier} is already stopped for {providerDisplayText}.", LidGuardPipeResponseMessageCodes.SessionIdAlreadyStoppedForProvider, [request.SessionIdentifier, providerDisplayText]);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
            return alreadyStoppedResponse;
        }

        var matchingProviderDisplayText = AgentProviderDisplay.CreateProviderDisplayText(request.Provider, request.ProviderName);
        return RemoveSnapshotsInsideGate(request, matchingSnapshots, $"Removed {matchingSnapshots.Length} session(s) matching {matchingProviderDisplayText} session id \"{request.SessionIdentifier}\".", LidGuardPipeResponseMessageCodes.SessionRemovedMatchingProviderSessionId, [matchingSnapshots.Length.ToString(), matchingProviderDisplayText, request.SessionIdentifier]);
    }

    private LidGuardPipeResponse RemoveSnapshotsInsideGate(LidGuardPipeRequest request, LidGuardSessionSnapshot[] matchingSnapshots, string multipleRemovalSuccessMessage, string multipleRemovalSuccessMessageCode, string[] multipleRemovalSuccessMessageArguments)
    {
        var lastStopResponse = CreateSuccessResponse(string.Empty);
        foreach (var matchingSnapshot in matchingSnapshots)
        {
            var sessionKey = matchingSnapshot.Key.ToString();
            var stopRequest = new LidGuardSessionStopRequest
            {
                SessionIdentifier = matchingSnapshot.SessionIdentifier,
                Provider = matchingSnapshot.Provider,
                ProviderName = matchingSnapshot.ProviderName
            };
            lastStopResponse = StopInsideGate(stopRequest, $"Removed {sessionKey}.", null, out _, "session-removed", LidGuardPipeCommands.RemoveSession, LidGuardPipeResponseMessageCodes.SessionRemoved, [sessionKey]);
            if (!lastStopResponse.Succeeded) return lastStopResponse;
        }

        var successMessage = matchingSnapshots.Length == 1 ? lastStopResponse.Message : multipleRemovalSuccessMessage;
        var successMessageCode = matchingSnapshots.Length == 1 ? lastStopResponse.MessageCode : multipleRemovalSuccessMessageCode;
        var successMessageArguments = matchingSnapshots.Length == 1 ? lastStopResponse.MessageArguments : multipleRemovalSuccessMessageArguments;
        if (matchingSnapshots.Length > 1 && TryExtractPostStopScheduleMessage(lastStopResponse.Message, out var postStopScheduleMessage)) successMessage = $"{successMessage} {postStopScheduleMessage}";
        return CreateSuccessResponse(successMessage, successMessageCode, successMessageArguments, lastStopResponse.SuspendScheduled, lastStopResponse.SuspendMode, lastStopResponse.SuspendDelaySeconds, lastStopResponse.SuspendReasonCode);
    }

    private LidGuardPipeResponse StopInsideGate(LidGuardSessionStopRequest request, string successMessage, LidGuardPipeRequest runtimeRequest, out StopFollowUpAwaitContext stopFollowUpAwaitContext, string eventName = "session-stopped", string commandName = LidGuardPipeCommands.Stop, string successMessageCode = "", string[] successMessageArguments = null)
    {
        stopFollowUpAwaitContext = null;
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var response = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-stop-rejected", request, response, commandName);
            return response;
        }

        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);
        if (request.HasPendingProviderWork) return DeferStopForPendingProviderWorkInsideGate(request, key, commandName);

        CancelWatcher(key);
        RemoveTranscriptMonitorSession(key);

        if (!_sessionRegistry.Stop(request, out var stoppedSnapshot))
        {
            var response = CreateSuccessResponse($"Session {key} is already stopped.", LidGuardPipeResponseMessageCodes.SessionAlreadyStopped, [key.ToString()]);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-already-stopped", request, response, commandName);
            return response;
        }

        if (HasSessionsKeepingProtectionAppliedInsideGate())
        {
            ReconfigureSessionTimeoutMonitorInsideGate();
            var response = CreateSuccessResponse(successMessage, successMessageCode, successMessageArguments);
            LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, response, stoppedSnapshot, commandName);
            QueuePostSessionEndWebhookIfRequired(request, stoppedSnapshot, eventName, commandName, _sessionRegistry.ActiveSessionCount);
            return response;
        }

        if (request.SuppressWebhooks && _pendingSuspendCancellationTokenSource is not null)
        {
            ReconfigureSessionTimeoutMonitorInsideGate();
            ReconfigureServerRuntimeCleanupInsideGate();
            var response = CreateSuccessResponse(successMessage, successMessageCode, successMessageArguments);
            LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, response, stoppedSnapshot, commandName);
            return response;
        }

        var pendingSuspendContext = CreatePendingSuspendContext(request, runtimeRequest, stoppedSnapshot, commandName);
        var successResponse = HandleSuspendAfterProtectionRetainedOrReleased(pendingSuspendContext, stoppedSnapshot, eventName, successMessage, successMessageCode, successMessageArguments, _sessionRegistry.ActiveSessionCount, out stopFollowUpAwaitContext, out var suspendScheduled);
        if (!successResponse.Succeeded)
        {
            ReconfigureSessionTimeoutMonitorInsideGate();
            ReconfigureServerRuntimeCleanupInsideGate();
            LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, successResponse, stoppedSnapshot, commandName);
            return successResponse;
        }

        ReconfigureSessionTimeoutMonitorInsideGate();
        if (!suspendScheduled) QueuePostSessionEndWebhookIfRequired(request, stoppedSnapshot, eventName, commandName, _sessionRegistry.ActiveSessionCount);
        ReconfigureServerRuntimeCleanupInsideGate();
        LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, successResponse, stoppedSnapshot, commandName);
        return successResponse;
    }

    private LidGuardPipeResponse DeferStopForPendingProviderWorkInsideGate(LidGuardSessionStopRequest request, LidGuardSessionKey key, string commandName)
    {
        CancelWatcher(key);

        var syntheticRequest = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = request.Provider,
            ProviderName = request.ProviderName,
            SessionIdentifier = request.SessionIdentifier,
            SessionStateReason = request.PendingProviderWorkReason
        };

        if (!_sessionRegistry.TryMarkPendingProviderWork(request.Provider, request.SessionIdentifier, request.ProviderName, request.PendingProviderWorkReason, out var snapshot))
        {
            var ignoredResponse = CreateSuccessResponse($"Session {key} is not active; ignored deferred stop for pending provider work.");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-stop-deferred-ignored", request, ignoredResponse, commandName);
            return ignoredResponse;
        }

        ResetTranscriptMonitorSession(key);
        CancelPendingSuspend();
        CancelServerRuntimeCleanupInsideGate();
        ReconfigureSessionTimeoutMonitorInsideGate();
        var protectionResult = EnsureProtection();
        if (!protectionResult.Succeeded)
        {
            var failedResponse = CreateFailureResponse(protectionResult);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-stop-deferred-failed", request, failedResponse, snapshot, commandName);
            return failedResponse;
        }

        var pendingProviderWorkReason = string.IsNullOrWhiteSpace(request.PendingProviderWorkReason) ? "pending provider work remains" : request.PendingProviderWorkReason.Trim();
        var response = CreateSuccessResponse($"Deferred stop for {key} because {pendingProviderWorkReason}.");
        LidGuardRuntimeLogWriter.AppendSessionLog("session-stop-deferred", syntheticRequest, response, snapshot);
        return response;
    }

    private StopFollowUpAwaitContext TryCreateStopFollowUpAwaitContextInsideGate(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, LidGuardPipeResponse scheduledResponse, int activeSessionCount, CancellationTokenSource pendingSuspendCancellationTokenSource)
    {
        if (!pendingSuspendContext.CanReturnStopContinuation) return null;
        if (pendingSuspendContext.StopHookAlreadyActive && !_settings.RepeatClosedLidStopFollowUp) return null;
        if (!ClosedLidStopFollowUpConfiguration.IsEnabled(_settings, out var followUpWebhookUrl)) return null;

        return new StopFollowUpAwaitContext(pendingSuspendContext, snapshot, eventName, scheduledResponse, activeSessionCount, followUpWebhookUrl, _settings.ClosedLidStopFollowUpDelaySeconds, pendingSuspendCancellationTokenSource, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private async Task<LidGuardPipeResponse> AwaitStopFollowUpReplyAsync(StopFollowUpAwaitContext stopFollowUpAwaitContext, CancellationToken cancellationToken)
    {
        using var followUpCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopFollowUpAwaitContext.PendingSuspendCancellationTokenSource.Token);
        try
        {
            var canStartFollowUp = await stopFollowUpAwaitContext.FollowUpStartReadySource.Task.WaitAsync(followUpCancellationTokenSource.Token);
            if (!canStartFollowUp)
            {
                await CompleteStopFollowUpAwaitAsync();
                return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.Canceled);
            }

            string userInterfaceCulture;
            string closedLidStopFollowUpSound;
            int? closedLidStopFollowUpSoundVolumeOverridePercent;
            DateTimeOffset replyDeadlineUtc;
            await _gate.WaitAsync(followUpCancellationTokenSource.Token);
            try
            {
                _pendingStopFollowUpStatus = StopFollowUpStatuses.AwaitingReply;
                userInterfaceCulture = LidGuardCulture.ResolveEffectiveCultureName(_settings);
                closedLidStopFollowUpSound = _settings.ClosedLidStopFollowUpSound;
                closedLidStopFollowUpSoundVolumeOverridePercent = _settings.ClosedLidStopFollowUpSoundVolumeOverridePercent;
                replyDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(stopFollowUpAwaitContext.ReplyWaitSeconds);
            }
            finally { _gate.Release(); }

            var stopFollowUpWebhookRequest = CreateStopFollowUpWebhookRequest(stopFollowUpAwaitContext.PendingSuspendContext, stopFollowUpAwaitContext.Snapshot, stopFollowUpAwaitContext.ActiveSessionCount, stopFollowUpAwaitContext.ReplyWaitSeconds, replyDeadlineUtc, userInterfaceCulture);
            var startResult = await StopFollowUpWebhookClient.StartAsync(stopFollowUpAwaitContext.FollowUpWebhookUrl, stopFollowUpWebhookRequest, followUpCancellationTokenSource.Token, s_stopFollowUpWebhookTimeout);
            if (!startResult.Succeeded)
            {
                await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
                await AppendStopFollowUpFailureAsync(stopFollowUpAwaitContext, StopFollowUpStatuses.WebhookFailed, startResult.Message);
                return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.WebhookFailed);
            }

            if (!TryResolveStopFollowUpPollUri(stopFollowUpAwaitContext.FollowUpWebhookUrl, startResult.Value.ReplyPollUrl, out var replyPollUri, out var pollUriMessage))
            {
                await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
                await AppendStopFollowUpFailureAsync(stopFollowUpAwaitContext, StopFollowUpStatuses.PollFailed, pollUriMessage);
                return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.PollFailed);
            }

            if (startResult.Value.ExpiresAtUtc < replyDeadlineUtc) replyDeadlineUtc = startResult.Value.ExpiresAtUtc;

            await PlayClosedLidStopFollowUpSoundAsync(stopFollowUpAwaitContext.PendingSuspendContext, stopFollowUpAwaitContext.Snapshot, stopFollowUpAwaitContext.EventName, closedLidStopFollowUpSound, closedLidStopFollowUpSoundVolumeOverridePercent, followUpCancellationTokenSource.Token);

            while (DateTimeOffset.UtcNow <= replyDeadlineUtc)
            {
                var pollResult = await StopFollowUpWebhookClient.PollAsync(replyPollUri, followUpCancellationTokenSource.Token, s_stopFollowUpWebhookTimeout);
                if (!pollResult.Succeeded)
                {
                    await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
                    await AppendStopFollowUpFailureAsync(stopFollowUpAwaitContext, StopFollowUpStatuses.PollFailed, pollResult.Message);
                    return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.PollFailed);
                }

                if (pollResult.Value.ExpiresAtUtc > replyDeadlineUtc) replyDeadlineUtc = pollResult.Value.ExpiresAtUtc;

                var pollStatus = pollResult.Value.Status?.Trim() ?? string.Empty;
                if (pollStatus.Equals("Answered", StringComparison.Ordinal))
                {
                    var reply = pollResult.Value.Reply?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(reply))
                    {
                        await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
                        await AppendStopFollowUpFailureAsync(stopFollowUpAwaitContext, StopFollowUpStatuses.PollFailed, "The closed-lid stop follow-up poll returned an empty reply.");
                        return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.PollFailed);
                    }

                    return await ResumeSessionAfterStopFollowUpReplyAsync(stopFollowUpAwaitContext, reply);
                }

                if (pollStatus.Equals("Expired", StringComparison.Ordinal))
                {
                    await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
                    return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.TimedOut);
                }

                if (pollStatus.Equals("Canceled", StringComparison.Ordinal))
                {
                    await CompleteStopFollowUpAwaitAsync();
                    return await ContinuePendingSuspendImmediatelyAfterStopFollowUpCancellationAsync(stopFollowUpAwaitContext);
                }

                if (!pollStatus.Equals("Pending", StringComparison.Ordinal))
                {
                    await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
                    await AppendStopFollowUpFailureAsync(stopFollowUpAwaitContext, StopFollowUpStatuses.PollFailed, $"The closed-lid stop follow-up poll returned an unknown status '{pollStatus}'.");
                    return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.PollFailed);
                }

                await Task.Delay(s_stopFollowUpPollInterval, followUpCancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await CompleteStopFollowUpAwaitAsync();
            return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.Canceled);
        }

        await ContinueSuspendAfterStopFollowUpAwaitAsync(stopFollowUpAwaitContext);
        return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.TimedOut);
    }

    private async Task<LidGuardPipeResponse> ContinuePendingSuspendImmediatelyAfterStopFollowUpCancellationAsync(StopFollowUpAwaitContext stopFollowUpAwaitContext)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var pendingSuspendStillMatches = ReferenceEquals(_pendingSuspendCancellationTokenSource, stopFollowUpAwaitContext.PendingSuspendCancellationTokenSource);
            if (!pendingSuspendStillMatches) return CreateStopFollowUpResponse(stopFollowUpAwaitContext.ScheduledResponse, StopFollowUpStatuses.Canceled);

            CancelPendingSuspend(true);
            var immediatePendingSuspendCancellationTokenSource = new CancellationTokenSource();
            _pendingSuspendCancellationTokenSource = immediatePendingSuspendCancellationTokenSource;
            _pendingStopFollowUpStatus = string.Empty;
            var response = CreateSuccessResponse($"Canceled the ask-before-sleep reply wait. Scheduled {stopFollowUpAwaitContext.ScheduledResponse.SuspendMode} {DescribePostStopSuspendDelay(0)} {DescribeSuspendReason(stopFollowUpAwaitContext.ActiveSessionCount)}", stopFollowUpAwaitContext.ScheduledResponse.MessageCode, stopFollowUpAwaitContext.ScheduledResponse.MessageArguments, true, stopFollowUpAwaitContext.ScheduledResponse.SuspendMode, 0, stopFollowUpAwaitContext.ScheduledResponse.SuspendReasonCode, stopFollowUpStatus: StopFollowUpStatuses.Canceled);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{stopFollowUpAwaitContext.EventName}-stop-follow-up-canceled-suspend-immediate", stopFollowUpAwaitContext.PendingSuspendContext, response, stopFollowUpAwaitContext.Snapshot);
            _ = SuspendAfterDelayAsync(stopFollowUpAwaitContext.PendingSuspendContext, stopFollowUpAwaitContext.Snapshot, stopFollowUpAwaitContext.EventName, CreateSuspendWebhookReason(stopFollowUpAwaitContext.ActiveSessionCount), stopFollowUpAwaitContext.ActiveSessionCount, 0, immediatePendingSuspendCancellationTokenSource, null);
            return response;
        }
        finally { _gate.Release(); }
    }

    private async Task<LidGuardPipeResponse> ResumeSessionAfterStopFollowUpReplyAsync(StopFollowUpAwaitContext stopFollowUpAwaitContext, string reply)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            _pendingStopFollowUpStatus = string.Empty;
            CancelPendingSuspend(true);

            RestoreTranscriptMonitorSession(stopFollowUpAwaitContext.Snapshot);
            var startRequest = new LidGuardSessionStartRequest
            {
                SessionIdentifier = stopFollowUpAwaitContext.Snapshot.SessionIdentifier,
                Provider = stopFollowUpAwaitContext.Snapshot.Provider,
                ProviderName = stopFollowUpAwaitContext.Snapshot.ProviderName,
                StartedAt = stopFollowUpAwaitContext.Snapshot.StartedAt,
                LastActivityAt = DateTimeOffset.UtcNow,
                WatchedProcessIdentifier = stopFollowUpAwaitContext.Snapshot.WatchedProcessIdentifier,
                WatchRegistrationKind = stopFollowUpAwaitContext.Snapshot.WatchRegistrationKind,
                InputPromptPreview = stopFollowUpAwaitContext.Snapshot.InputPromptPreview,
                WorkingDirectory = stopFollowUpAwaitContext.Snapshot.WorkingDirectory,
                TranscriptPath = stopFollowUpAwaitContext.Snapshot.TranscriptPath
            };
            var restoredSnapshot = _sessionRegistry.StartOrUpdate(startRequest);
            CancelServerRuntimeCleanupInsideGate();
            StartWatcher(restoredSnapshot);
            ReconfigureSessionTimeoutMonitorInsideGate();
            EnsureEmergencyHibernationThermalMonitor();
            var protectionResult = EnsureProtection();
            var responseMessage = protectionResult.Succeeded ? $"Blocked stop for {restoredSnapshot.Key} because a closed-lid stop follow-up reply arrived." : $"Blocked stop for {restoredSnapshot.Key} because a closed-lid stop follow-up reply arrived. {CreateResultMessage(protectionResult)}";
            var response = CreateSuccessResponse(responseMessage, stopContinuationRequested: true, stopContinuationPrompt: reply, stopFollowUpStatus: StopFollowUpStatuses.ReplyReceived);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{stopFollowUpAwaitContext.EventName}-stop-follow-up-reply-received", stopFollowUpAwaitContext.PendingSuspendContext, response, restoredSnapshot);
            return response;
        }
        finally { _gate.Release(); }
    }

    private LidGuardPipeResponse HandleSuspendAfterProtectionRetainedOrReleased(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, string successMessage, string successMessageCode, string[] successMessageArguments, int activeSessionCount, out StopFollowUpAwaitContext stopFollowUpAwaitContext, out bool suspendScheduled)
    {
        stopFollowUpAwaitContext = null;
        suspendScheduled = false;
        var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("suspend");
        if (!closedLidPolicyApplicability.IsApplicable)
        {
            var releaseResult = ReleaseProtectionIfNoSessionRequiresItInsideGate(eventName, pendingSuspendContext, snapshot, "Released LidGuard protection because pending suspend is not applicable.");
            if (!releaseResult.Succeeded) return CreateFailureResponse(releaseResult);

            var response = CreateSuccessResponse(closedLidPolicyApplicability.Message);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-skipped", pendingSuspendContext, response, snapshot);
            return CreateSuccessResponse(successMessage);
        }

        LogSuspendProtectionRetainedInsideGate(eventName, pendingSuspendContext, snapshot);
        var suspendMode = _settings.SuspendMode;
        var postStopSuspendDelaySeconds = GetPostStopSuspendDelaySeconds(pendingSuspendContext);
        var scheduledResponse = CreateSuccessResponse($"Scheduled {suspendMode} {DescribePostStopSuspendDelay(postStopSuspendDelaySeconds)} {DescribeSuspendReason(activeSessionCount)}");
        LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-scheduled", pendingSuspendContext, scheduledResponse, snapshot);
        suspendScheduled = true;
        CancelPendingSuspend();
        var pendingSuspendCancellationTokenSource = new CancellationTokenSource();
        _pendingSuspendCancellationTokenSource = pendingSuspendCancellationTokenSource;
        _pendingStopFollowUpStatus = string.Empty;
        stopFollowUpAwaitContext = TryCreateStopFollowUpAwaitContextInsideGate(pendingSuspendContext, snapshot, eventName, scheduledResponse, activeSessionCount, pendingSuspendCancellationTokenSource);
        _ = SuspendAfterDelayAsync(pendingSuspendContext, snapshot, eventName, CreateSuspendWebhookReason(activeSessionCount), activeSessionCount, postStopSuspendDelaySeconds, pendingSuspendCancellationTokenSource, stopFollowUpAwaitContext);
        var suspendReasonCode = activeSessionCount == 0 ? LidGuardPipeResponseMessageCodes.SuspendReasonCompleted : LidGuardPipeResponseMessageCodes.SuspendReasonSoftLocked;
        return CreateSuccessResponse($"{successMessage} Scheduled {suspendMode} {DescribePostStopSuspendDelay(postStopSuspendDelaySeconds)} {DescribeSuspendReason(activeSessionCount)}", successMessageCode, successMessageArguments, true, suspendMode, postStopSuspendDelaySeconds, suspendReasonCode);
    }

    private async Task SuspendAfterDelayAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, SuspendWebhookReason suspendWebhookReason, int suspendTriggerSessionCount, int postStopSuspendDelaySeconds, CancellationTokenSource pendingSuspendCancellationTokenSource, StopFollowUpAwaitContext stopFollowUpAwaitContext)
    {
        var preSuspendWebhookAttempted = false;
        try
        {
            if (postStopSuspendDelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(postStopSuspendDelaySeconds), pendingSuspendCancellationTokenSource.Token);

            var postStopSuspendSound = string.Empty;
            var postStopSuspendSoundVolumeOverridePercent = (int?)null;
            await _gate.WaitAsync(pendingSuspendCancellationTokenSource.Token);
            try
            {
                if (HasSessionsKeepingProtectionAppliedInsideGate())
                {
                    CancelStopFollowUpBeforeStart(stopFollowUpAwaitContext);
                    var canceledResponse = CreateSuccessResponse("Skipped pending suspend because a session became active before suspend ran.");
                    LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                    QueuePostSessionEndWebhookForCanceledSuspendInsideGate(pendingSuspendContext, snapshot, eventName, _sessionRegistry.ActiveSessionCount);
                    return;
                }

                var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("suspend");
                if (!closedLidPolicyApplicability.IsApplicable)
                {
                    CancelStopFollowUpBeforeStart(stopFollowUpAwaitContext);
                    var releaseResult = ReleaseProtectionIfNoSessionRequiresItInsideGate(eventName, pendingSuspendContext, snapshot, "Released LidGuard protection because pending suspend was canceled before the pre-suspend webhook.");
                    if (!releaseResult.Succeeded)
                    {
                        var failedResponse = CreateFailureResponse(releaseResult);
                        LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, failedResponse, snapshot);
                        return;
                    }

                    var canceledResponse = CreateSuccessResponse(closedLidPolicyApplicability.Message);
                    LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                    QueuePostSessionEndWebhookForCanceledSuspendInsideGate(pendingSuspendContext, snapshot, eventName, _sessionRegistry.ActiveSessionCount);
                    return;
                }

                postStopSuspendSound = _settings.PostStopSuspendSound;
                postStopSuspendSoundVolumeOverridePercent = _settings.PostStopSuspendSoundVolumeOverridePercent;
                SignalStopFollowUpStartReady(stopFollowUpAwaitContext);
            }
            finally { _gate.Release(); }

            if (stopFollowUpAwaitContext is not null) await stopFollowUpAwaitContext.FollowUpCompletedSource.Task.WaitAsync(pendingSuspendCancellationTokenSource.Token);

            await _gate.WaitAsync(pendingSuspendCancellationTokenSource.Token);
            try
            {
                var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("suspend");
                if (!closedLidPolicyApplicability.IsApplicable)
                {
                    var releaseResult = ReleaseProtectionIfNoSessionRequiresItInsideGate(eventName, pendingSuspendContext, snapshot, "Released LidGuard protection because pending suspend was canceled after the stop follow-up ended because the lid is no longer closed.");
                    if (!releaseResult.Succeeded)
                    {
                        var failedResponse = CreateFailureResponse(releaseResult);
                        LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, failedResponse, snapshot);
                        return;
                    }

                    var canceledResponse = CreateSuccessResponse(closedLidPolicyApplicability.Message);
                    LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                    QueuePostSessionEndWebhookForCanceledSuspendInsideGate(pendingSuspendContext, snapshot, eventName, _sessionRegistry.ActiveSessionCount);
                    return;
                }
            }
            finally { _gate.Release(); }

            preSuspendWebhookAttempted = true;
            await SendPreSuspendWebhookAsync(pendingSuspendContext, snapshot, eventName, suspendWebhookReason, suspendTriggerSessionCount, pendingSuspendCancellationTokenSource.Token);
            await PlayPostStopSuspendSoundAsync(pendingSuspendContext, snapshot, eventName, postStopSuspendSound, postStopSuspendSoundVolumeOverridePercent, pendingSuspendCancellationTokenSource.Token);
            await RequestSuspendAsync(pendingSuspendContext, snapshot, eventName, suspendWebhookReason, suspendTriggerSessionCount, pendingSuspendCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (!preSuspendWebhookAttempted)
        {
            var suppressPostSessionEndWebhook = await ShouldSuppressPostSessionEndWebhookOnPendingSuspendCancellationAsync(pendingSuspendCancellationTokenSource);
            if (!suppressPostSessionEndWebhook) await QueuePostSessionEndWebhookForCanceledSuspendAsync(pendingSuspendContext, snapshot, eventName);
        }
        catch (OperationCanceledException) { }
        finally { await ClearPendingSuspendAsync(pendingSuspendCancellationTokenSource); }
    }

    private async Task RequestSuspendAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, SuspendWebhookReason suspendWebhookReason, int suspendTriggerSessionCount, CancellationToken cancellationToken)
    {
        var suspendMode = SystemSuspendMode.Sleep;
        var suspendHistoryEntryCount = (int?)null;
        var activeSessionCount = 0;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (HasSessionsKeepingProtectionAppliedInsideGate())
            {
                var canceledResponse = CreateSuccessResponse("Skipped pending suspend because a session became active before suspend was requested.");
                LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                return;
            }

            var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("suspend");
            if (!closedLidPolicyApplicability.IsApplicable)
            {
                var releaseResult = ReleaseProtectionIfNoSessionRequiresItInsideGate(eventName, pendingSuspendContext, snapshot, "Released LidGuard protection because pending suspend was canceled before the suspend request.");
                if (!releaseResult.Succeeded)
                {
                    var failedResponse = CreateFailureResponse(releaseResult);
                    LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, failedResponse, snapshot);
                    return;
                }

                var canceledResponse = CreateSuccessResponse(closedLidPolicyApplicability.Message);
                LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                return;
            }

            ReleaseSuspendProtectionInsideGate(eventName, pendingSuspendContext, snapshot, "Released LidGuard protection immediately before requesting suspend.");
            suspendMode = _settings.SuspendMode;
            suspendHistoryEntryCount = _settings.SuspendHistoryEntryCount;
            activeSessionCount = _sessionRegistry.ActiveSessionCount;
            var requestingResponse = CreateSuccessResponse($"Requesting {suspendMode} {DescribeSuspendReason(activeSessionCount)}");
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-requesting", pendingSuspendContext, requestingResponse, snapshot);
        }
        finally { _gate.Release(); }

        var suspendResult = _systemSuspendService.Suspend(suspendMode);
        var suspendHistoryEntry = new SuspendHistoryEntry
        {
            RecordedAt = DateTimeOffset.UtcNow,
            SuspendMode = suspendMode,
            Reason = suspendWebhookReason,
            Succeeded = suspendResult.Succeeded,
            Message = suspendResult.Succeeded ? CreateSuspendHistorySuccessMessage(suspendResult, $"Requested {suspendMode} {DescribeSuspendReason(activeSessionCount)}") : CreateResultMessage(suspendResult),
            EventName = suspendResult.Succeeded ? $"{eventName}-suspend-requested" : $"{eventName}-suspend-failed",
            CommandName = pendingSuspendContext.CommandName,
            Provider = pendingSuspendContext.Provider,
            ProviderName = pendingSuspendContext.ProviderName,
            SessionIdentifier = pendingSuspendContext.SessionIdentifier,
            WorkingDirectory = pendingSuspendContext.WorkingDirectory,
            SessionStateReason = pendingSuspendContext.SessionStateReason,
            ActiveSessionCount = activeSessionCount,
            SuspendTriggerSessionCount = suspendTriggerSessionCount
        };
        SuspendHistoryLogStore.Append(suspendHistoryEntry, suspendHistoryEntryCount);
        if (suspendResult.Succeeded)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                var response = CreateSuccessResponse(CreateSuspendHistorySuccessMessage(suspendResult, $"Requested {suspendMode} {DescribeSuspendReason(activeSessionCount)}"));
                LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-requested", pendingSuspendContext, response, snapshot);
            }
            finally { _gate.Release(); }

            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(suspendResult);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-failed", pendingSuspendContext, response, snapshot);
        }
        finally { _gate.Release(); }
    }

    private void CancelWatcher(LidGuardSessionKey key)
    {
        if (!_watcherCancellationTokenSources.Remove(key, out var cancellationTokenSource)) return;

        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }

    private void CancelPendingSuspend(bool suppressCanceledSuspendPostSessionEndWebhook = false)
    {
        if (_pendingSuspendCancellationTokenSource is null) return;

        if (suppressCanceledSuspendPostSessionEndWebhook) _pendingSuspendCancellationTokenSourcesSuppressingPostSessionEndWebhook.Add(_pendingSuspendCancellationTokenSource);
        else _pendingSuspendCancellationTokenSourcesSuppressingPostSessionEndWebhook.Remove(_pendingSuspendCancellationTokenSource);
        _pendingStopFollowUpStatus = string.Empty;
        _pendingSuspendCancellationTokenSource.Cancel();
        _pendingSuspendCancellationTokenSource = null;
    }

    private async Task ClearPendingSuspendAsync(CancellationTokenSource pendingSuspendCancellationTokenSource)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (ReferenceEquals(_pendingSuspendCancellationTokenSource, pendingSuspendCancellationTokenSource)) _pendingSuspendCancellationTokenSource = null;
            _pendingSuspendCancellationTokenSourcesSuppressingPostSessionEndWebhook.Remove(pendingSuspendCancellationTokenSource);
            _pendingStopFollowUpStatus = string.Empty;
            ReconfigureServerRuntimeCleanupInsideGate(false);
        }
        finally { _gate.Release(); }

        pendingSuspendCancellationTokenSource.Dispose();
    }

    private async Task PlayPostStopSuspendSoundAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, string postStopSuspendSound, int? postStopSuspendSoundVolumeOverridePercent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(postStopSuspendSound)) return;

        var playbackResult = await _soundPlaybackCoordinator.PlayAsync(postStopSuspendSound, postStopSuspendSoundVolumeOverridePercent, "Post-stop suspend sound", cancellationToken);
        foreach (var volumeWarningResult in playbackResult.VolumeWarningResults) await AppendPostStopSuspendSoundVolumeWarningAsync(pendingSuspendContext, snapshot, eventName, volumeWarningResult);

        if (playbackResult.PlaybackResult.Succeeded)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                var response = CreateSuccessResponse($"Played post-stop suspend sound: {postStopSuspendSound}.");
                LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-sound-played", pendingSuspendContext, response, snapshot);
            }
            finally { _gate.Release(); }

            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(playbackResult.PlaybackResult);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-sound-failed", pendingSuspendContext, response, snapshot);
        }
        finally { _gate.Release(); }
    }

    private async Task PlayClosedLidStopFollowUpSoundAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, string closedLidStopFollowUpSound, int? closedLidStopFollowUpSoundVolumeOverridePercent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(closedLidStopFollowUpSound)) return;

        var playbackResult = await _soundPlaybackCoordinator.PlayAsync(closedLidStopFollowUpSound, closedLidStopFollowUpSoundVolumeOverridePercent, "Closed-lid stop follow-up sound", cancellationToken);
        foreach (var volumeWarningResult in playbackResult.VolumeWarningResults) await AppendClosedLidStopFollowUpSoundVolumeWarningAsync(pendingSuspendContext, snapshot, eventName, volumeWarningResult);

        if (playbackResult.PlaybackResult.Succeeded)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                var response = CreateSuccessResponse($"Played closed-lid stop follow-up sound: {closedLidStopFollowUpSound}.");
                LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-stop-follow-up-sound-played", pendingSuspendContext, response, snapshot);
            }
            finally { _gate.Release(); }

            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(playbackResult.PlaybackResult);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-stop-follow-up-sound-failed", pendingSuspendContext, response, snapshot);
        }
        finally { _gate.Release(); }
    }

    private async Task AppendPostStopSuspendSoundVolumeWarningAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, LidGuardOperationResult volumeWarningResult)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateSuccessResponse($"Warning: {CreateResultMessage(volumeWarningResult)}");
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-sound-volume-warning", pendingSuspendContext, response, snapshot);
        }
        finally { _gate.Release(); }
    }

    private async Task AppendClosedLidStopFollowUpSoundVolumeWarningAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, LidGuardOperationResult volumeWarningResult)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateSuccessResponse($"Warning: {CreateResultMessage(volumeWarningResult)}");
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-stop-follow-up-sound-volume-warning", pendingSuspendContext, response, snapshot);
        }
        finally { _gate.Release(); }
    }

    private async Task SendPreSuspendWebhookAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, SuspendWebhookReason suspendWebhookReason, int suspendTriggerSessionCount, CancellationToken cancellationToken)
    {
        if (pendingSuspendContext.SuppressWebhooks) return;

        string preSuspendWebhookUrl;
        string userInterfaceCulture;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            preSuspendWebhookUrl = _settings.PreSuspendWebhookUrl;
            userInterfaceCulture = LidGuardCulture.ResolveEffectiveCultureName(_settings);
        }
        finally { _gate.Release(); }

        var webhookRequest = CreatePreSuspendWebhookRequest(pendingSuspendContext, snapshot, suspendWebhookReason, suspendTriggerSessionCount, userInterfaceCulture);
        var sendResult = await SuspendWebhookSender.SendAsync(preSuspendWebhookUrl, webhookRequest, cancellationToken, s_preSuspendWebhookTimeout);
        if (sendResult.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(preSuspendWebhookUrl))
            {
                await _gate.WaitAsync(CancellationToken.None);
                try
                {
                    var response = CreateSuccessResponse("Sent pre-suspend webhook.");
                    LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-webhook-sent", pendingSuspendContext, response, snapshot);
                }
                finally { _gate.Release(); }
            }

            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(sendResult);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-webhook-failed", pendingSuspendContext, response, snapshot);
        }
        finally { _gate.Release(); }
    }

    private static LidGuardWebhookRequest CreatePreSuspendWebhookRequest(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, SuspendWebhookReason suspendWebhookReason, int suspendTriggerSessionCount, string userInterfaceCulture)
    {
        var webhookRequest = new LidGuardWebhookRequest
        {
            EventType = LidGuardWebhookEventTypes.PreSuspend,
            Reason = suspendWebhookReason.ToString(),
            UserInterfaceCulture = userInterfaceCulture,
            SoftLockedSessionCount = suspendWebhookReason == SuspendWebhookReason.SoftLocked ? suspendTriggerSessionCount : null
        };

        if (!pendingSuspendContext.IsProviderSessionEnd) return webhookRequest;

        return CreateSessionEndWebhookRequest(LidGuardWebhookEventTypes.PreSuspend, suspendWebhookReason.ToString(), snapshot, string.IsNullOrWhiteSpace(pendingSuspendContext.SessionEndReason) ? pendingSuspendContext.CommandName : pendingSuspendContext.SessionEndReason, suspendTriggerSessionCount, pendingSuspendContext.ProviderSessionEndedAt ?? DateTimeOffset.UtcNow, userInterfaceCulture, suspendWebhookReason == SuspendWebhookReason.SoftLocked ? suspendTriggerSessionCount : null, pendingSuspendContext.LastAssistantMessage);
    }

    private static LidGuardWebhookRequest CreateStopFollowUpWebhookRequest(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, int activeSessionCount, int replyWaitSeconds, DateTimeOffset replyDeadlineUtc, string userInterfaceCulture)
    {
        var webhookRequest = CreateSessionEndWebhookRequest(LidGuardWebhookEventTypes.StopFollowUp, LidGuardWebhookReasons.AwaitingReply, snapshot, string.IsNullOrWhiteSpace(pendingSuspendContext.SessionEndReason) ? pendingSuspendContext.CommandName : pendingSuspendContext.SessionEndReason, activeSessionCount, pendingSuspendContext.ProviderSessionEndedAt ?? DateTimeOffset.UtcNow, userInterfaceCulture, null, pendingSuspendContext.LastAssistantMessage);
        return new LidGuardWebhookRequest
        {
            EventType = webhookRequest.EventType,
            Reason = webhookRequest.Reason,
            UserInterfaceCulture = webhookRequest.UserInterfaceCulture,
            SoftLockedSessionCount = webhookRequest.SoftLockedSessionCount,
            Provider = webhookRequest.Provider,
            ProviderName = webhookRequest.ProviderName,
            SessionIdentifier = webhookRequest.SessionIdentifier,
            StartedAtUtc = webhookRequest.StartedAtUtc,
            LastActivityAtUtc = webhookRequest.LastActivityAtUtc,
            EndedAtUtc = webhookRequest.EndedAtUtc,
            EndReason = webhookRequest.EndReason,
            ActiveSessionCount = webhookRequest.ActiveSessionCount,
            InputPromptPreview = webhookRequest.InputPromptPreview,
            LastAssistantMessage = webhookRequest.LastAssistantMessage,
            ReplyWaitSeconds = replyWaitSeconds,
            ReplyDeadlineUtc = replyDeadlineUtc,
            WorkingDirectory = webhookRequest.WorkingDirectory,
            TranscriptPath = webhookRequest.TranscriptPath
        };
    }

    private static LidGuardWebhookRequest CreateSessionEndWebhookRequest(string eventType, string reason, LidGuardSessionSnapshot snapshot, string endReason, int activeSessionCount, DateTimeOffset endedAtUtc, string userInterfaceCulture, int? softLockedSessionCount = null, string lastAssistantMessage = "") => new()
    {
        EventType = eventType,
        Reason = reason,
        UserInterfaceCulture = userInterfaceCulture,
        SoftLockedSessionCount = softLockedSessionCount,
        Provider = snapshot.Provider.ToString(),
        ProviderName = string.IsNullOrWhiteSpace(snapshot.ProviderName) ? null : snapshot.ProviderName,
        SessionIdentifier = snapshot.SessionIdentifier,
        StartedAtUtc = snapshot.StartedAt,
        LastActivityAtUtc = snapshot.LastActivityAt,
        EndedAtUtc = endedAtUtc,
        EndReason = endReason,
        ActiveSessionCount = activeSessionCount,
        InputPromptPreview = string.IsNullOrWhiteSpace(snapshot.InputPromptPreview) ? null : snapshot.InputPromptPreview,
        LastAssistantMessage = string.IsNullOrWhiteSpace(lastAssistantMessage) ? AgentTranscriptAssistantMessageExtractor.CreateLastAssistantMessage(snapshot.Provider, snapshot.TranscriptPath) : lastAssistantMessage,
        WorkingDirectory = string.IsNullOrWhiteSpace(snapshot.WorkingDirectory) ? null : snapshot.WorkingDirectory,
        TranscriptPath = string.IsNullOrWhiteSpace(snapshot.TranscriptPath) ? null : snapshot.TranscriptPath
    };

    private static bool TryResolveStopFollowUpPollUri(string webhookUrl, string replyPollUrl, out Uri replyPollUri, out string message)
    {
        replyPollUri = null;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(replyPollUrl))
        {
            message = "The closed-lid stop follow-up webhook returned an empty replyPollUrl.";
            return false;
        }

        if (Uri.TryCreate(replyPollUrl.Trim(), UriKind.Absolute, out replyPollUri))
        {
            if (replyPollUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || replyPollUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return true;

            message = "The closed-lid stop follow-up webhook returned a replyPollUrl with an unsupported scheme.";
            return false;
        }

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri))
        {
            message = "The closed-lid stop follow-up webhook URL could not be parsed while resolving replyPollUrl.";
            return false;
        }

        if (!Uri.TryCreate(webhookUri, replyPollUrl.Trim(), out replyPollUri))
        {
            message = "The closed-lid stop follow-up webhook returned an invalid relative replyPollUrl.";
            return false;
        }

        if (replyPollUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || replyPollUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return true;

        message = "The closed-lid stop follow-up webhook returned a relative replyPollUrl with an unsupported scheme.";
        return false;
    }

    private async Task CompleteStopFollowUpAwaitAsync()
    {
        await _gate.WaitAsync(CancellationToken.None);
        try { _pendingStopFollowUpStatus = string.Empty; }
        finally { _gate.Release(); }
    }

    private async Task ContinueSuspendAfterStopFollowUpAwaitAsync(StopFollowUpAwaitContext stopFollowUpAwaitContext)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            _pendingStopFollowUpStatus = string.Empty;
            var pendingSuspendStillMatches = ReferenceEquals(_pendingSuspendCancellationTokenSource, stopFollowUpAwaitContext.PendingSuspendCancellationTokenSource);
            if (pendingSuspendStillMatches && !stopFollowUpAwaitContext.PendingSuspendCancellationTokenSource.IsCancellationRequested) stopFollowUpAwaitContext.FollowUpCompletedSource.TrySetResult();
        }
        finally { _gate.Release(); }
    }

    private static void CancelStopFollowUpBeforeStart(StopFollowUpAwaitContext stopFollowUpAwaitContext) => stopFollowUpAwaitContext?.FollowUpStartReadySource.TrySetResult(false);

    private static void SignalStopFollowUpStartReady(StopFollowUpAwaitContext stopFollowUpAwaitContext) => stopFollowUpAwaitContext?.FollowUpStartReadySource.TrySetResult(true);

    private async Task<bool> ShouldSuppressPostSessionEndWebhookOnPendingSuspendCancellationAsync(CancellationTokenSource pendingSuspendCancellationTokenSource)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try { return _pendingSuspendCancellationTokenSourcesSuppressingPostSessionEndWebhook.Contains(pendingSuspendCancellationTokenSource); }
        finally { _gate.Release(); }
    }

    private int GetPostStopSuspendDelaySeconds(PendingSuspendContext pendingSuspendContext) => pendingSuspendContext.CanReturnStopContinuation && pendingSuspendContext.StopHookAlreadyActive && !_settings.RepeatClosedLidStopFollowUp ? 0 : _settings.PostStopSuspendDelaySeconds;

    private async Task AppendStopFollowUpFailureAsync(StopFollowUpAwaitContext stopFollowUpAwaitContext, string stopFollowUpStatus, string message)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateSuccessResponse(message, stopFollowUpStatus: stopFollowUpStatus);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{stopFollowUpAwaitContext.EventName}-stop-follow-up-{stopFollowUpStatus}", stopFollowUpAwaitContext.PendingSuspendContext, response, stopFollowUpAwaitContext.Snapshot);
        }
        finally { _gate.Release(); }
    }

    private LidGuardPipeResponse CreateStopFollowUpResponse(LidGuardPipeResponse scheduledResponse, string stopFollowUpStatus)
        => CreateSuccessResponse(scheduledResponse.Message, scheduledResponse.MessageCode, scheduledResponse.MessageArguments, scheduledResponse.SuspendScheduled, scheduledResponse.SuspendMode, scheduledResponse.SuspendDelaySeconds, scheduledResponse.SuspendReasonCode, stopFollowUpStatus: stopFollowUpStatus);

    private async Task QueuePostSessionEndWebhookForCanceledSuspendAsync(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try { QueuePostSessionEndWebhookForCanceledSuspendInsideGate(pendingSuspendContext, snapshot, eventName, _sessionRegistry.ActiveSessionCount); }
        finally { _gate.Release(); }
    }

    private void QueuePostSessionEndWebhookForCanceledSuspendInsideGate(PendingSuspendContext pendingSuspendContext, LidGuardSessionSnapshot snapshot, string eventName, int activeSessionCount)
    {
        if (pendingSuspendContext.SuppressWebhooks) return;
        if (!pendingSuspendContext.IsProviderSessionEnd) return;

        var stopRequest = new LidGuardSessionStopRequest
        {
            Provider = snapshot.Provider,
            ProviderName = snapshot.ProviderName,
            SessionIdentifier = snapshot.SessionIdentifier,
            IsProviderSessionEnd = true,
            SessionEndReason = pendingSuspendContext.SessionEndReason,
            LastAssistantMessage = pendingSuspendContext.LastAssistantMessage
        };
        QueuePostSessionEndWebhookInsideGate(stopRequest, snapshot, eventName, pendingSuspendContext.CommandName, activeSessionCount, pendingSuspendContext.ProviderSessionEndedAt ?? DateTimeOffset.UtcNow);
    }

    private void QueuePostSessionEndWebhookIfRequired(LidGuardSessionStopRequest request, LidGuardSessionSnapshot snapshot, string eventName, string commandName, int activeSessionCount)
    {
        if (request.SuppressWebhooks) return;
        if (!request.IsProviderSessionEnd) return;
        QueuePostSessionEndWebhookInsideGate(request, snapshot, eventName, commandName, activeSessionCount, DateTimeOffset.UtcNow);
    }

    private void QueuePostSessionEndWebhookInsideGate(LidGuardSessionStopRequest request, LidGuardSessionSnapshot snapshot, string eventName, string commandName, int activeSessionCount, DateTimeOffset endedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(_settings.PostSessionEndWebhookUrl)) return;

        var postSessionEndWebhookUrl = _settings.PostSessionEndWebhookUrl;
        var userInterfaceCulture = LidGuardCulture.ResolveEffectiveCultureName(_settings);
        var webhookRequest = CreateSessionEndWebhookRequest(LidGuardWebhookEventTypes.PostSessionEnd, LidGuardWebhookReasons.SessionEnded, snapshot, string.IsNullOrWhiteSpace(request.SessionEndReason) ? commandName : request.SessionEndReason, activeSessionCount, endedAtUtc, userInterfaceCulture, lastAssistantMessage: request.LastAssistantMessage);

        _pendingPostSessionEndWebhookCount++;
        _ = SendPostSessionEndWebhookAsync(postSessionEndWebhookUrl, webhookRequest, request, snapshot, eventName, commandName, activeSessionCount);
    }

    private async Task SendPostSessionEndWebhookAsync(string postSessionEndWebhookUrl, LidGuardWebhookRequest webhookRequest, LidGuardSessionStopRequest request, LidGuardSessionSnapshot snapshot, string eventName, string commandName, int activeSessionCount)
    {
        try
        {
            LidGuardOperationResult sendResult;
            try { sendResult = await SuspendWebhookSender.SendPostSessionEndAsync(postSessionEndWebhookUrl, webhookRequest, CancellationToken.None, s_postSessionEndWebhookTimeout); }
            catch (Exception exception) { sendResult = LidGuardOperationResult.Failure($"Failed to send the post-session-end webhook: {exception.Message}"); }

            if (sendResult.Succeeded)
            {
                var successResponse = LidGuardPipeResponse.Success("Sent post-session-end webhook.", activeSessionCount, [], _settings);
                LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-post-session-end-webhook-sent", request, successResponse, snapshot, commandName);
                return;
            }

            var response = LidGuardPipeResponse.Failure(sendResult.Message, activeSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-post-session-end-webhook-failed", request, response, snapshot, commandName);
        }
        finally { await CompletePostSessionEndWebhookAsync(); }
    }

    private async Task CompletePostSessionEndWebhookAsync()
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_pendingPostSessionEndWebhookCount > 0) _pendingPostSessionEndWebhookCount--;
            ReconfigureServerRuntimeCleanupInsideGate(false);
        }
        finally { _gate.Release(); }
    }

    private async Task HandleEmergencyHibernationThresholdReachedAsync(EmergencyHibernationThermalThresholdReachedContext emergencyHibernationThermalThresholdReachedContext)
    {
        var emergencyHibernationTemperatureCelsius = emergencyHibernationThermalThresholdReachedContext.ThresholdTemperatureCelsius;
        var emergencyHibernationTemperatureMode = emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureMode;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (!TryValidateEmergencyHibernationStateInsideGate(emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, out emergencyHibernationTemperatureCelsius, out emergencyHibernationTemperatureMode, out var canceledMessage))
            {
                LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-canceled", CreateSuccessResponse(canceledMessage), emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
                return;
            }

            CancelPendingSuspend(true);
            LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-thermal-detected", CreateSuccessResponse($"Detected high system temperature {DescribeEmergencyHibernationTemperature(emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode)}. Requesting Emergency Hibernation."), emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
        }
        finally { _gate.Release(); }

        await SendEmergencyHibernationWebhookAsync(emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
        await RequestEmergencyHibernationAsync(emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
    }

    private async Task SendEmergencyHibernationWebhookAsync(int observedTemperatureCelsius, int emergencyHibernationTemperatureCelsius, EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        string preSuspendWebhookUrl;
        string userInterfaceCulture;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            preSuspendWebhookUrl = _settings.PreSuspendWebhookUrl;
            userInterfaceCulture = LidGuardCulture.ResolveEffectiveCultureName(_settings);
        }
        finally { _gate.Release(); }

        var sendResult = await SuspendWebhookSender.SendAsync(preSuspendWebhookUrl, SuspendWebhookReason.EmergencyHibernation, 0, userInterfaceCulture, CancellationToken.None, s_emergencyHibernationWebhookTimeout);
        if (sendResult.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(preSuspendWebhookUrl))
            {
                await _gate.WaitAsync(CancellationToken.None);
                try { LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-webhook-sent", CreateSuccessResponse("Sent Emergency Hibernation webhook."), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode); }
                finally { _gate.Release(); }
            }

            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try { LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-webhook-failed", CreateFailureResponse(sendResult), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode); }
        finally { _gate.Release(); }
    }

    private LidGuardOperationResult ReleaseEmergencyHibernationProtectionInsideGate(int observedTemperatureCelsius, int emergencyHibernationTemperatureCelsius, EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        if (!_protectionCoordinator.IsApplied) return LidGuardOperationResult.Success();

        var restoreResult = RestoreProtection();
        var response = restoreResult.Succeeded ? CreateSuccessResponse("Released LidGuard protection immediately before requesting Emergency Hibernation.") : CreateFailureResponse(restoreResult);
        LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-suspend-protection-released", response, observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
        return restoreResult;
    }

    private async Task RequestEmergencyHibernationAsync(int observedTemperatureCelsius, int emergencyHibernationTemperatureCelsius, EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        var suspendHistoryEntryCount = (int?)null;
        var activeSessionCount = 0;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (!TryValidateEmergencyHibernationStateInsideGate(observedTemperatureCelsius, out emergencyHibernationTemperatureCelsius, out emergencyHibernationTemperatureMode, out var canceledMessage))
            {
                LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-canceled", CreateSuccessResponse(canceledMessage), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
                return;
            }

            suspendHistoryEntryCount = _settings.SuspendHistoryEntryCount;
            activeSessionCount = _sessionRegistry.ActiveSessionCount;
            ReleaseEmergencyHibernationProtectionInsideGate(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
            LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-requesting", CreateSuccessResponse($"Requesting Emergency Hibernation because system temperature reached {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode)}."), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
        }
        finally { _gate.Release(); }

        var emergencyHibernationTemperatureDescription = DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
        var hibernationResult = _systemSuspendService.Suspend(SystemSuspendMode.Hibernate);
        AppendEmergencyHibernationSuspendHistory(suspendHistoryEntryCount, activeSessionCount, observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode, SystemSuspendMode.Hibernate, hibernationResult, $"Requested Emergency Hibernation because system temperature reached {emergencyHibernationTemperatureDescription}.", "emergency-hibernation-requested", "emergency-hibernation-failed");
        if (hibernationResult.Succeeded)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-requested", CreateSuccessResponse(CreateSuspendHistorySuccessMessage(hibernationResult, $"Requested Emergency Hibernation because system temperature reached {emergencyHibernationTemperatureDescription}.")), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
            }
            finally { _gate.Release(); }

            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try { LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-failed", CreateFailureResponse(hibernationResult), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode); }
        finally { _gate.Release(); }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-sleep-fallback-requesting", CreateSuccessResponse($"Emergency Hibernation failed. Requesting Sleep fallback because system temperature reached {emergencyHibernationTemperatureDescription}."), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
        }
        finally { _gate.Release(); }

        var sleepFallbackResult = _systemSuspendService.Suspend(SystemSuspendMode.Sleep);
        AppendEmergencyHibernationSuspendHistory(suspendHistoryEntryCount, activeSessionCount, observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode, SystemSuspendMode.Sleep, sleepFallbackResult, $"Requested Sleep fallback after Emergency Hibernation failed because system temperature reached {emergencyHibernationTemperatureDescription}.", "emergency-hibernation-sleep-fallback-requested", "emergency-hibernation-sleep-fallback-failed");
        if (sleepFallbackResult.Succeeded)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-sleep-fallback-requested", CreateSuccessResponse(CreateSuspendHistorySuccessMessage(sleepFallbackResult, $"Requested Sleep fallback after Emergency Hibernation failed because system temperature reached {emergencyHibernationTemperatureDescription}.")), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode);
            }
            finally { _gate.Release(); }

            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try { LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog("emergency-hibernation-sleep-fallback-failed", CreateFailureResponse(sleepFallbackResult), observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode); }
        finally { _gate.Release(); }
    }

    private static void AppendEmergencyHibernationSuspendHistory(int? suspendHistoryEntryCount, int activeSessionCount, int observedTemperatureCelsius, int emergencyHibernationTemperatureCelsius, EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode, SystemSuspendMode suspendMode, LidGuardOperationResult suspendResult, string successMessage, string successEventName, string failureEventName)
    {
        var message = suspendResult.Succeeded ? CreateSuspendHistorySuccessMessage(suspendResult, successMessage) : CreateResultMessage(suspendResult);
        var eventName = suspendResult.Succeeded ? successEventName : failureEventName;
        var suspendHistoryEntry = new SuspendHistoryEntry
        {
            RecordedAt = DateTimeOffset.UtcNow,
            SuspendMode = suspendMode,
            Reason = SuspendWebhookReason.EmergencyHibernation,
            Succeeded = suspendResult.Succeeded,
            Message = message,
            EventName = eventName,
            CommandName = "emergency-hibernation-monitor",
            ActiveSessionCount = activeSessionCount,
            ObservedTemperatureCelsius = observedTemperatureCelsius,
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius,
            EmergencyHibernationTemperatureMode = emergencyHibernationTemperatureMode
        };
        SuspendHistoryLogStore.Append(suspendHistoryEntry, suspendHistoryEntryCount);
    }

    private EmergencyHibernationThermalMonitorState CreateEmergencyHibernationThermalMonitorState()
    {
        var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("Emergency Hibernation");
        return new EmergencyHibernationThermalMonitorState(_protectionCoordinator.IsApplied, _settings.EmergencyHibernationOnHighTemperature, closedLidPolicyApplicability.IsApplicable, closedLidPolicyApplicability.LidSwitchState, closedLidPolicyApplicability.VisibleDisplayMonitorCount, _settings.EmergencyHibernationTemperatureMode, LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius(_settings.EmergencyHibernationTemperatureCelsius));
    }

    private bool TryValidateEmergencyHibernationStateInsideGate(int observedTemperatureCelsius, out int emergencyHibernationTemperatureCelsius, out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode, out string message)
    {
        emergencyHibernationTemperatureCelsius = LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius(_settings.EmergencyHibernationTemperatureCelsius);
        emergencyHibernationTemperatureMode = _settings.EmergencyHibernationTemperatureMode;
        message = string.Empty;

        if (!_protectionCoordinator.IsApplied)
        {
            message = "Skipped Emergency Hibernation because guard protection is no longer applied.";
            return false;
        }

        if (!_settings.EmergencyHibernationOnHighTemperature)
        {
            message = "Skipped Emergency Hibernation because high-temperature Emergency Hibernation is disabled.";
            return false;
        }

        var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("Emergency Hibernation");
        if (!closedLidPolicyApplicability.IsApplicable)
        {
            message = closedLidPolicyApplicability.Message;
            return false;
        }

        if (observedTemperatureCelsius < emergencyHibernationTemperatureCelsius)
        {
            message =
                $"Skipped Emergency Hibernation because the observed temperature {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode)} is no longer above the current threshold.";
            return false;
        }

        return true;
    }

    private LidGuardPipeResponse CreateSuccessResponse(string message, string messageCode = "", string[] messageArguments = null, bool suspendScheduled = false, SystemSuspendMode suspendMode = SystemSuspendMode.Sleep, int suspendDelaySeconds = 0, string suspendReasonCode = "", bool stopContinuationRequested = false, string stopContinuationPrompt = "", string stopFollowUpStatus = "")
    {
        var snapshots = _sessionRegistry.GetSnapshots();
        var currentLidAndDisplayState = GetCurrentLidAndDisplayState();
        return LidGuardPipeResponse.Success(message, snapshots.Count, CreateSessionStatuses(snapshots), _settings, currentLidAndDisplayState.LidSwitchState, currentLidAndDisplayState.VisibleDisplayMonitorCount, messageCode, messageArguments, suspendScheduled, suspendMode, suspendDelaySeconds, suspendReasonCode, stopContinuationRequested, stopContinuationPrompt, string.IsNullOrWhiteSpace(stopFollowUpStatus) ? _pendingStopFollowUpStatus : stopFollowUpStatus);
    }

    private LidGuardPipeResponse CreateFailureResponse(LidGuardOperationResult result)
    {
        var snapshots = _sessionRegistry.GetSnapshots();
        var currentLidAndDisplayState = GetCurrentLidAndDisplayState();
        return LidGuardPipeResponse.Failure(CreateResultMessage(result), snapshots.Count, false, currentLidAndDisplayState.LidSwitchState, currentLidAndDisplayState.VisibleDisplayMonitorCount);
    }

    private static LidGuardSessionStatus[] CreateSessionStatuses(IReadOnlyList<LidGuardSessionSnapshot> snapshots)
    {
        var statuses = new LidGuardSessionStatus[snapshots.Count];
        for (var snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
        {
            var snapshot = snapshots[snapshotIndex];
            statuses[snapshotIndex] = new LidGuardSessionStatus
            {
                SessionIdentifier = snapshot.SessionIdentifier,
                Provider = snapshot.Provider,
                ProviderName = snapshot.ProviderName,
                StartedAt = snapshot.StartedAt,
                LastActivityAt = snapshot.LastActivityAt,
                SoftLockState = snapshot.SoftLockState,
                SoftLockReason = snapshot.SoftLockReason,
                SoftLockedAt = snapshot.SoftLockedAt,
                HasPendingProviderWork = snapshot.HasPendingProviderWork,
                PendingProviderWorkReason = snapshot.PendingProviderWorkReason,
                WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
                WorkingDirectory = snapshot.WorkingDirectory
            };
        }

        return statuses;
    }

    private static string CreateResultMessage(LidGuardOperationResult result)
    {
        if (result.NativeErrorCode == 0) return result.Message;
        return $"{result.Message} Native error: {result.NativeErrorCode}.";
    }

    private static string CreateSuspendHistorySuccessMessage(LidGuardOperationResult result, string defaultMessage) => string.IsNullOrWhiteSpace(result.Message) ? defaultMessage : $"{defaultMessage} {result.Message}";

    private ClosedLidPolicyApplicability EvaluateClosedLidPolicyApplicability(string actionName)
    {
        var lidSwitchState = _lidStateSource.CurrentState;
        var visibleDisplayMonitorCount = _visibleDisplayMonitorCountProvider.GetVisibleDisplayMonitorCount(excludeInternalDisplayMonitors: lidSwitchState == LidSwitchState.Closed);
        return EvaluateClosedLidPolicyApplicability(actionName, new CurrentLidAndDisplayState(lidSwitchState, visibleDisplayMonitorCount));
    }

    private CurrentLidAndDisplayState GetCurrentLidAndDisplayState()
    {
        var lidSwitchState = _lidStateSource.CurrentState;
        return new(lidSwitchState, _visibleDisplayMonitorCountProvider.GetVisibleDisplayMonitorCount());
    }

    private static ClosedLidPolicyApplicability EvaluateClosedLidPolicyApplicability(string actionName, CurrentLidAndDisplayState currentLidAndDisplayState)
    {
        if (currentLidAndDisplayState.LidSwitchState == LidSwitchState.Open) return new ClosedLidPolicyApplicability(false, currentLidAndDisplayState.LidSwitchState, currentLidAndDisplayState.VisibleDisplayMonitorCount, $"Skipped {actionName} because the lid is open.");

        if (currentLidAndDisplayState.LidSwitchState != LidSwitchState.Closed) return new ClosedLidPolicyApplicability(false, currentLidAndDisplayState.LidSwitchState, currentLidAndDisplayState.VisibleDisplayMonitorCount, $"Skipped {actionName} because the lid state is {currentLidAndDisplayState.LidSwitchState}.");

        if (currentLidAndDisplayState.VisibleDisplayMonitorCount > 0)
        {
            return new ClosedLidPolicyApplicability(false, currentLidAndDisplayState.LidSwitchState, currentLidAndDisplayState.VisibleDisplayMonitorCount, $"Skipped {actionName} because {currentLidAndDisplayState.VisibleDisplayMonitorCount} visible display monitor(s) are active while the lid is closed.");
        }

        return new ClosedLidPolicyApplicability(true, currentLidAndDisplayState.LidSwitchState, currentLidAndDisplayState.VisibleDisplayMonitorCount, string.Empty);
    }

    private static string DescribePostStopSuspendDelay(int postStopSuspendDelaySeconds) => postStopSuspendDelaySeconds == 0 ? "immediately" : $"in {postStopSuspendDelaySeconds} second(s)";

    private static SuspendWebhookReason CreateSuspendWebhookReason(int activeSessionCount) => activeSessionCount == 0 ? SuspendWebhookReason.Completed : SuspendWebhookReason.SoftLocked;

    private static string DescribeSuspendReason(int activeSessionCount)
        => activeSessionCount == 0 ? "because the lid is closed, no suspend-blocking visible display monitors remain, and the last session stopped." : "because the lid is closed, no suspend-blocking visible display monitors remain, and all remaining sessions are soft-locked.";

    private static string DescribeEmergencyHibernationTemperature(int observedTemperatureCelsius, int emergencyHibernationTemperatureCelsius, EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
        => $"{observedTemperatureCelsius} Celsius using {emergencyHibernationTemperatureMode} mode (threshold: {emergencyHibernationTemperatureCelsius} Celsius)";

    private CleanupResult CleanupWatchedProcessExitInsideGate(LidGuardSessionSnapshot snapshot, string commandName, string eventName)
    {
        var sessionKey = snapshot.Key.ToString();
        var successMessage = eventName == "watched-process-exited" ? $"Watched process exited for {sessionKey}." : $"Cleaned orphan session {sessionKey}.";
        var successMessageCode = eventName == "watched-process-exited" ? LidGuardPipeResponseMessageCodes.WatchedProcessExited : LidGuardPipeResponseMessageCodes.WatchedProcessOrphanCleaned;
        var stopRequest = new LidGuardSessionStopRequest
        {
            SessionIdentifier = snapshot.SessionIdentifier,
            Provider = snapshot.Provider,
            ProviderName = snapshot.ProviderName,
            SuppressWebhooks = true
        };
        var stopResponse = StopInsideGate(stopRequest, successMessage, null, out _, eventName, commandName, successMessageCode, [sessionKey]);
        var removedSessionCount = stopResponse.Succeeded && !stopResponse.Message.Contains("already stopped", StringComparison.Ordinal) ? 1 : 0;
        return new CleanupResult(stopResponse, removedSessionCount);
    }

    private static bool TryExtractPostStopScheduleMessage(string responseMessage, out string postStopScheduleMessage)
    {
        postStopScheduleMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(responseMessage)) return false;

        var scheduledIndex = responseMessage.IndexOf("Scheduled ", StringComparison.Ordinal);
        if (scheduledIndex < 0) return false;

        postStopScheduleMessage = responseMessage[scheduledIndex..];
        return true;
    }

    private LidGuardPipeResponse MarkSessionActiveInsideGate(LidGuardPipeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var rejectedResponse = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-activity-rejected", request, rejectedResponse);
            return rejectedResponse;
        }

        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);
        if (!_sessionRegistry.TryMarkActive(request.Provider, request.SessionIdentifier, request.ProviderName, out var snapshot, out var changed))
        {
            var ignoredResponse = CreateSuccessResponse($"Session {key} is not active; ignored activity signal.");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-activity-ignored", request, ignoredResponse);
            return ignoredResponse;
        }

        ResetTranscriptMonitorSession(key);
        CancelPendingSuspend();
        CancelServerRuntimeCleanupInsideGate();
        ReconfigureSessionTimeoutMonitorInsideGate();
        var protectionResult = EnsureProtection();
        if (!protectionResult.Succeeded)
        {
            var failedResponse = CreateFailureResponse(protectionResult);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-activity-failed", request, failedResponse, snapshot);
            return failedResponse;
        }

        var successMessage = changed ? $"Cleared soft-lock for {key} because activity was detected from {request.SessionStateReason}." : $"Session {key} was already active.";
        var successResponse = CreateSuccessResponse(successMessage);
        LidGuardRuntimeLogWriter.AppendSessionLog("session-activity-recorded", request, successResponse, snapshot);
        return successResponse;
    }

    private AgentTranscriptMonitoringRegistrationResult RegisterTranscriptMonitor(LidGuardPipeRequest request)
    {
        if (!TryGetTranscriptMonitor(request.Provider, out var transcriptMonitor)) return new AgentTranscriptMonitoringRegistrationResult();
        return transcriptMonitor.RegisterOrUpdateSession(request.SessionIdentifier, request.ProviderName, request.WorkingDirectory, request.TranscriptPath);
    }

    private void ResetTranscriptMonitorSession(LidGuardSessionKey sessionKey)
    {
        if (TryGetTranscriptMonitor(sessionKey.Provider, out var transcriptMonitor)) transcriptMonitor.ResetSessionObservationBaseline(sessionKey);
    }

    private void RemoveTranscriptMonitorSession(LidGuardSessionKey sessionKey)
    {
        if (TryGetTranscriptMonitor(sessionKey.Provider, out var transcriptMonitor)) transcriptMonitor.RemoveSession(sessionKey);
    }

    private void RestoreTranscriptMonitorSession(LidGuardSessionSnapshot snapshot)
    {
        if (!TryGetTranscriptMonitor(snapshot.Provider, out var transcriptMonitor)) return;
        transcriptMonitor.RegisterOrUpdateSession(snapshot.SessionIdentifier, snapshot.ProviderName, snapshot.WorkingDirectory, snapshot.TranscriptPath);
    }

    private bool TryGetTranscriptMonitor(AgentProvider provider, out AgentTranscriptMonitor transcriptMonitor)
    {
        if (provider == AgentProvider.Codex)
        {
            transcriptMonitor = _codexTranscriptMonitor;
            return true;
        }

        if (provider == AgentProvider.Claude)
        {
            transcriptMonitor = _claudeTranscriptMonitor;
            return true;
        }

        if (provider == AgentProvider.GitHubCopilot)
        {
            transcriptMonitor = _gitHubCopilotTranscriptMonitor;
            return true;
        }

        transcriptMonitor = null;
        return false;
    }

    private void AppendTranscriptMonitorRegistration(LidGuardPipeRequest request, LidGuardSessionSnapshot snapshot, AgentTranscriptMonitoringRegistrationResult transcriptMonitoringRegistrationResult)
    {
        if (!IsTranscriptMonitoringProvider(request.Provider)) return;
        if (string.IsNullOrWhiteSpace(transcriptMonitoringRegistrationResult.Message)) return;

        var response = CreateSuccessResponse(transcriptMonitoringRegistrationResult.Message);
        var eventNamePrefix = GetTranscriptMonitorEventNamePrefix(request.Provider);
        var eventName = transcriptMonitoringRegistrationResult.MonitoringEnabled ? $"{eventNamePrefix}-transcript-monitor-configured" : $"{eventNamePrefix}-transcript-monitor-skipped";
        LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, response, snapshot);
    }

    private static bool IsTranscriptMonitoringProvider(AgentProvider provider) => provider is AgentProvider.Codex or AgentProvider.Claude or AgentProvider.GitHubCopilot;

    private static string GetTranscriptMonitorEventNamePrefix(AgentProvider provider) => provider == AgentProvider.GitHubCopilot ? "github-copilot" : provider.ToString().ToLowerInvariant();

    private async Task HandleTranscriptActivityDetectedAsync(AgentTranscriptActivityDetectedContext transcriptActivityDetectedContext)
    {
        var request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.MarkSessionActive,
            Provider = transcriptActivityDetectedContext.SessionKey.Provider,
            ProviderName = transcriptActivityDetectedContext.SessionKey.ProviderName,
            SessionIdentifier = transcriptActivityDetectedContext.SessionKey.SessionIdentifier,
            SessionStateReason = transcriptActivityDetectedContext.ActivityReason,
            WorkingDirectory = transcriptActivityDetectedContext.WorkingDirectory,
            TranscriptPath = transcriptActivityDetectedContext.TranscriptPath
        };

        await _gate.WaitAsync(CancellationToken.None);
        try { MarkSessionActiveInsideGate(request); }
        finally { _gate.Release(); }
    }

    private async Task HandleTranscriptStopDetectedAsync(AgentTranscriptStopDetectedContext transcriptStopDetectedContext)
    {
        var request = new LidGuardSessionStopRequest
        {
            Provider = transcriptStopDetectedContext.SessionKey.Provider,
            SessionIdentifier = transcriptStopDetectedContext.SessionKey.SessionIdentifier,
            ProviderName = transcriptStopDetectedContext.SessionKey.ProviderName
        };

        await _gate.WaitAsync(CancellationToken.None);
        try { StopInsideGate(request, $"Stopped {transcriptStopDetectedContext.SessionKey} because {transcriptStopDetectedContext.StopReasonDescription}.", null, out _, transcriptStopDetectedContext.StopCommandName, transcriptStopDetectedContext.StopCommandName); }
        finally { _gate.Release(); }
    }

    private async Task HandleTranscriptSoftLockDetectedAsync(AgentTranscriptSoftLockDetectedContext transcriptSoftLockDetectedContext)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            MarkSessionSoftLockedInsideGate(transcriptSoftLockDetectedContext.SoftLockCommandName, transcriptSoftLockDetectedContext.SoftLockEventName, transcriptSoftLockDetectedContext.SessionKey.Provider, transcriptSoftLockDetectedContext.SessionKey.ProviderName, transcriptSoftLockDetectedContext.SessionKey.SessionIdentifier, transcriptSoftLockDetectedContext.SoftLockReason, transcriptSoftLockDetectedContext.SessionKey);
        }
        finally { _gate.Release(); }
    }

    private static AgentTranscriptMonitoringProfile CreateCodexTranscriptMonitoringProfile() => new()
    {
        Provider = AgentProvider.Codex,
        DisplayName = "Codex",
        FallbackRootDescription = "Codex sessions",
        FallbackRootPathResolver = GetCodexSessionsDirectoryPath,
        StopDetector = AgentTranscriptStopDetectors.IsLastCodexTranscriptLineTurnAborted,
        SoftLockDetector = AgentTranscriptSoftLockDetectors.HasPendingCodexRequestUserInput,
        ActivityReason = "codex_transcript_activity_detected",
        StopCommandName = CodexTranscriptTurnAbortedCommandName,
        StopReasonDescription = "the Codex transcript reported turn_aborted",
        SoftLockCommandName = CodexTranscriptRequestUserInputPendingCommandName,
        SoftLockEventName = "codex-transcript-softlock-recorded"
    };

    private static AgentTranscriptMonitoringProfile CreateClaudeTranscriptMonitoringProfile() => new()
    {
        Provider = AgentProvider.Claude,
        DisplayName = "Claude",
        FallbackRootDescription = "Claude projects",
        FallbackRootPathResolver = GetClaudeProjectsDirectoryPath,
        StopDetector = AgentTranscriptStopDetectors.IsLastClaudeTranscriptLineInterrupted,
        ActivityReason = "claude_transcript_activity_detected",
        StopCommandName = ClaudeTranscriptInterruptedCommandName,
        StopReasonDescription = "the Claude transcript reported an interrupted request"
    };

    private static AgentTranscriptMonitoringProfile CreateGitHubCopilotTranscriptMonitoringProfile() => new()
    {
        Provider = AgentProvider.GitHubCopilot,
        DisplayName = "GitHub Copilot",
        FallbackRootDescription = "GitHub Copilot session-state",
        FallbackRootPathResolver = GetGitHubCopilotSessionStateDirectoryPath,
        FallbackTranscriptPathResolver = ResolveGitHubCopilotSessionEventsJsonlPath,
        StopDetector = AgentTranscriptStopDetectors.IsLastGitHubCopilotSessionEventAbort,
        ActivityReason = "github_copilot_session_event_activity_detected",
        StopCommandName = GitHubCopilotTranscriptAbortCommandName,
        StopReasonDescription = "the GitHub Copilot session event log reported abort"
    };

    private static string GetCodexSessionsDirectoryPath()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath)) return string.Empty;
        return Path.Combine(userProfilePath, ".codex", "sessions");
    }

    private static string GetClaudeProjectsDirectoryPath()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath)) return string.Empty;
        return Path.Combine(userProfilePath, ".claude", "projects");
    }

    private static string GetGitHubCopilotSessionStateDirectoryPath()
    {
        var gitHubCopilotHomeDirectoryPath = GetGitHubCopilotHomeDirectoryPath();
        if (string.IsNullOrWhiteSpace(gitHubCopilotHomeDirectoryPath)) return string.Empty;
        return Path.Combine(gitHubCopilotHomeDirectoryPath, "session-state");
    }

    private static string ResolveGitHubCopilotSessionEventsJsonlPath(string sessionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return string.Empty;
        if (sessionIdentifier.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return string.Empty;

        var gitHubCopilotSessionStateDirectoryPath = GetGitHubCopilotSessionStateDirectoryPath();
        if (string.IsNullOrWhiteSpace(gitHubCopilotSessionStateDirectoryPath)) return string.Empty;
        return Path.Combine(gitHubCopilotSessionStateDirectoryPath, sessionIdentifier, "events.jsonl");
    }

    private static string GetGitHubCopilotHomeDirectoryPath()
    {
        var gitHubCopilotHomeDirectoryPath = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(gitHubCopilotHomeDirectoryPath)) return gitHubCopilotHomeDirectoryPath;

        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath)) return string.Empty;
        return Path.Combine(userProfilePath, ".copilot");
    }

    private static bool IsProcessRunning(int processIdentifier)
    {
        try
        {
            using var process = Process.GetProcessById(processIdentifier);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static PendingSuspendContext CreatePendingSuspendContext(LidGuardPipeRequest request, LidGuardSessionSnapshot snapshot)
        => new(request.Provider, AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName), request.SessionIdentifier, snapshot.WorkingDirectory, request.Command, request.SessionStateReason, false, false, string.Empty, null, string.Empty, false, false);

    private static PendingSuspendContext CreatePendingSuspendContext(LidGuardSessionStopRequest request, LidGuardPipeRequest runtimeRequest, LidGuardSessionSnapshot snapshot, string commandName)
        => new(request.Provider, AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName), request.SessionIdentifier, snapshot.WorkingDirectory, commandName, string.Empty, request.IsProviderSessionEnd, request.SuppressWebhooks, request.SessionEndReason, request.IsProviderSessionEnd ? DateTimeOffset.UtcNow : null, runtimeRequest?.LastAssistantMessage ?? request.LastAssistantMessage ?? string.Empty, runtimeRequest?.CanReturnStopContinuation ?? false, runtimeRequest?.StopHookAlreadyActive ?? false);

    private readonly record struct CleanupResult(LidGuardPipeResponse Response, int RemovedSessionCount);

    private sealed record StopFollowUpAwaitContext(PendingSuspendContext PendingSuspendContext, LidGuardSessionSnapshot Snapshot, string EventName, LidGuardPipeResponse ScheduledResponse, int ActiveSessionCount, string FollowUpWebhookUrl, int ReplyWaitSeconds, CancellationTokenSource PendingSuspendCancellationTokenSource, TaskCompletionSource<bool> FollowUpStartReadySource, TaskCompletionSource FollowUpCompletedSource);

    private readonly record struct ClosedLidPolicyApplicability(bool IsApplicable, LidSwitchState LidSwitchState, int VisibleDisplayMonitorCount, string Message);

    private readonly record struct CurrentLidAndDisplayState(LidSwitchState LidSwitchState, int VisibleDisplayMonitorCount);

    private readonly record struct WatchedProcessResolution(int ProcessIdentifier, LidGuardSessionWatchRegistrationKind WatchRegistrationKind)
    {
        public static WatchedProcessResolution None { get; } = new(0, LidGuardSessionWatchRegistrationKind.None);
    }
}

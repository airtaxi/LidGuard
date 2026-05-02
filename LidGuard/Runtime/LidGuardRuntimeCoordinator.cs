using System.Diagnostics;
using LidGuard.Ipc;
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
    private readonly ICommandLineProcessResolver _commandLineProcessResolver;
    private readonly IProcessExitWatcher _processExitWatcher;
    private readonly PostStopSuspendSoundPlaybackCoordinator _postStopSuspendSoundPlaybackCoordinator;
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

    private LidGuardSettings _settings;
    private CancellationTokenSource _pendingSuspendCancellationTokenSource;
    private CancellationTokenSource _sessionTimeoutCancellationTokenSource;
    private CancellationTokenSource _serverRuntimeCleanupCancellationTokenSource;
    private int _pendingPostSessionEndWebhookCount;
    private bool _serverRuntimeStopRequested;

    public LidGuardRuntimeCoordinator(
        LidGuardSettings initialSettings,
        IPowerRequestService powerRequestService,
        ICommandLineProcessResolver commandLineProcessResolver,
        IProcessExitWatcher processExitWatcher,
        LidActionPolicyController lidActionPolicyController,
        ISystemSuspendService systemSuspendService,
        IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer,
        ISystemAudioVolumeController systemAudioVolumeController,
        ILidStateSource lidStateSource,
        IVisibleDisplayMonitorCountProvider visibleDisplayMonitorCountProvider,
        Action requestRuntimeStop = null)
    {
        _commandLineProcessResolver = commandLineProcessResolver;
        _processExitWatcher = processExitWatcher;
        _postStopSuspendSoundPlaybackCoordinator = new PostStopSuspendSoundPlaybackCoordinator(postStopSuspendSoundPlayer, systemAudioVolumeController);
        _systemSuspendService = systemSuspendService;
        _lidStateSource = lidStateSource;
        _visibleDisplayMonitorCountProvider = visibleDisplayMonitorCountProvider;
        _protectionCoordinator = new LidGuardProtectionCoordinator(powerRequestService, lidActionPolicyController);
        _codexTranscriptMonitor = new AgentTranscriptMonitor(
            CreateCodexTranscriptMonitoringProfile(),
            HandleTranscriptActivityDetectedAsync,
            HandleTranscriptStopDetectedAsync,
            HandleTranscriptSoftLockDetectedAsync);
        _claudeTranscriptMonitor = new AgentTranscriptMonitor(
            CreateClaudeTranscriptMonitoringProfile(),
            HandleTranscriptActivityDetectedAsync,
            HandleTranscriptStopDetectedAsync,
            HandleTranscriptSoftLockDetectedAsync);
        _gitHubCopilotTranscriptMonitor = new AgentTranscriptMonitor(
            CreateGitHubCopilotTranscriptMonitoringProfile(),
            HandleTranscriptActivityDetectedAsync,
            HandleTranscriptStopDetectedAsync,
            HandleTranscriptSoftLockDetectedAsync);
        _emergencyHibernationThermalMonitor = new EmergencyHibernationThermalMonitor(
            CreateEmergencyHibernationThermalMonitorState,
            HandleEmergencyHibernationThresholdReachedAsync);
        _requestRuntimeStop = requestRuntimeStop;
        _settings = LidGuardSettings.Normalize(initialSettings);
    }

    public async Task<LidGuardPipeResponse> HandleAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Command switch
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
        finally
        {
            _gate.Release();
        }
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

            var transcriptMonitoringRegistrationResult = RegisterTranscriptMonitor(request);
            var watchedProcessResolution = ResolveWatchedProcess(request);
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
                WorkingDirectory = request.WorkingDirectory,
                TranscriptPath = transcriptMonitoringRegistrationResult.ResolvedTranscriptPath
            };

            var snapshot = _sessionRegistry.StartOrUpdate(startRequest);
            CancelServerRuntimeCleanupInsideGate();
            var protectionResult = EnsureProtection();
            if (!protectionResult.Succeeded)
            {
                RemoveTranscriptMonitorSession(snapshot.Key);
                _sessionRegistry.Stop(
                    new LidGuardSessionStopRequest
                    {
                        SessionIdentifier = request.SessionIdentifier,
                        Provider = request.Provider,
                        ProviderName = request.ProviderName
                    },
                    out _);
                var response = CreateFailureResponse(protectionResult);
                LidGuardRuntimeLogWriter.AppendSessionLog("session-start-failed", request, response);
                return response;
            }

            CancelPendingSuspend();
            StartWatcher(snapshot);
            ReconfigureSessionTimeoutMonitorInsideGate();
            AppendTranscriptMonitorRegistration(request, snapshot, transcriptMonitoringRegistrationResult);

            var watchMessage = CreateWatcherStatusMessage(request, snapshot);
            var successResponse = CreateSuccessResponse($"Started {snapshot.Key}.{watchMessage}");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-started", request, successResponse, snapshot);
            return successResponse;
        }
        finally
        {
            _gate.Release();
        }
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

            var successResponse = CreateSuccessResponse("Updated LidGuard runtime settings.");
            LidGuardRuntimeLogWriter.AppendSessionLog("settings-updated", request, successResponse);
            return successResponse;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LidGuardPipeResponse> StopAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stopRequest = new LidGuardSessionStopRequest
            {
                SessionIdentifier = request.SessionIdentifier,
                Provider = request.Provider,
                ProviderName = request.ProviderName,
                IsProviderSessionEnd = request.IsProviderSessionEnd,
                SessionEndReason = request.SessionEndReason
            };
            return StopInsideGate(stopRequest, $"Stopped {new LidGuardSessionKey(stopRequest.Provider, stopRequest.SessionIdentifier, stopRequest.ProviderName)}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LidGuardPipeResponse> MarkSessionActiveAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return MarkSessionActiveInsideGate(request);
        }
        finally
        {
            _gate.Release();
        }
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
            return MarkSessionSoftLockedInsideGate(
                LidGuardPipeCommands.MarkSessionSoftLocked,
                "session-softlock-recorded",
                request.Provider,
                request.ProviderName,
                request.SessionIdentifier,
                request.SessionStateReason,
                key);
        }
        finally
        {
            _gate.Release();
        }
    }

    private LidGuardPipeResponse MarkSessionSoftLockedInsideGate(
        string commandName,
        string eventName,
        AgentProvider provider,
        string providerName,
        string sessionIdentifier,
        string sessionStateReason,
        LidGuardSessionKey sessionKey)
    {
        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = provider,
            ProviderName = providerName,
            SessionIdentifier = sessionIdentifier,
            SessionStateReason = sessionStateReason
        };

        if (!_sessionRegistry.TryMarkSoftLocked(
            provider,
            sessionIdentifier,
            providerName,
            request.SessionStateReason,
            DateTimeOffset.UtcNow,
            out var snapshot,
            out var changed))
        {
            var ignoredResponse = CreateSuccessResponse($"Session {sessionKey} is not active; ignored soft-lock signal.");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-softlock-ignored", request, ignoredResponse);
            return ignoredResponse;
        }

        ResetTranscriptMonitorSession(sessionKey);
        var successMessage = changed
            ? $"Marked {sessionKey} as soft-locked from {request.SessionStateReason}."
            : $"Session {sessionKey} is already soft-locked from {snapshot.SoftLockReason}.";
        if (HasSessionsKeepingProtectionAppliedInsideGate())
        {
            var successResponse = CreateSuccessResponse(successMessage);
            LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, successResponse, snapshot);
            return successResponse;
        }

        var restoreResult = RestoreProtection();
        if (!restoreResult.Succeeded)
        {
            var failedResponse = CreateFailureResponse(restoreResult);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-softlock-failed", request, failedResponse, snapshot);
            return failedResponse;
        }

        var pendingSuspendContext = CreatePendingSuspendContext(request, snapshot);
        var successResponseWithSuspendPlan = HandleSuspendAfterProtectionReleased(
            pendingSuspendContext,
            snapshot,
            eventName,
            successMessage,
            _sessionRegistry.ActiveSessionCount,
            out _);
        LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, successResponseWithSuspendPlan, snapshot);
        return successResponseWithSuspendPlan;
    }

    private async Task<LidGuardPipeResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return CreateSuccessResponse("LidGuard runtime is running.");
        }
        finally
        {
            _gate.Release();
        }
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
            return StopInsideGate(
                stopRequest,
                $"Removed {new LidGuardSessionKey(stopRequest.Provider, stopRequest.SessionIdentifier, stopRequest.ProviderName)}.",
                "session-removed",
                LidGuardPipeCommands.RemoveSession);
        }
        finally
        {
            _gate.Release();
        }
    }

    private LidGuardPipeResponse RemoveAllSessionsInsideGate(LidGuardPipeRequest request)
    {
        var activeSnapshots = _sessionRegistry.GetSnapshots().ToArray();
        if (activeSnapshots.Length == 0)
        {
            var alreadyStoppedResponse = CreateSuccessResponse("There are no active sessions to remove.");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
            return alreadyStoppedResponse;
        }

        return RemoveSnapshotsInsideGate(request, activeSnapshots, $"Removed all {activeSnapshots.Length} active session(s).");
    }

    private async Task<LidGuardPipeResponse> CleanupOrphansAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var cleanupCount = 0;
            var cleanupFailureMessages = new List<string>();
            var cleanedCodexWorkingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in _sessionRegistry.GetSnapshots())
            {
                if (!snapshot.HasWatchedProcess) continue;
                if (IsProcessRunning(snapshot.WatchedProcessIdentifier)) continue;

                if (LidGuardWatchedProcessCleanup.ShouldCleanCodexWorkingDirectory(snapshot))
                {
                    var normalizedWorkingDirectory = LidGuardWatchedProcessCleanup.NormalizeWorkingDirectory(snapshot.WorkingDirectory);
                    if (!cleanedCodexWorkingDirectories.Add(normalizedWorkingDirectory)) continue;
                }

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

            var successResponse = CreateSuccessResponse($"Cleaned {cleanupCount} orphan session(s).");
            LidGuardRuntimeLogWriter.AppendRuntimeLog("cleanup-orphans-completed", LidGuardPipeCommands.CleanupOrphans, successResponse);
            return successResponse;
        }
        finally
        {
            _gate.Release();
        }
    }

    private WatchedProcessResolution ResolveWatchedProcess(LidGuardPipeRequest request)
    {
        if (request.WatchedProcessIdentifier > 0) return new WatchedProcessResolution(request.WatchedProcessIdentifier, LidGuardSessionWatchRegistrationKind.ExplicitWatchedProcessIdentifier);

        if (request.Provider == AgentProvider.Mcp) return WatchedProcessResolution.None;
        if (!_settings.WatchParentProcess) return WatchedProcessResolution.None;
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory)) return WatchedProcessResolution.None;

        var resolveResult = _commandLineProcessResolver.FindForWorkingDirectory(request.WorkingDirectory, request.Provider);
        if (!resolveResult.Succeeded) return WatchedProcessResolution.None;

        var resolvedCandidate = resolveResult.Value;
        if (request.Provider == AgentProvider.Codex && !resolvedCandidate.IsShellHosted) return WatchedProcessResolution.None;

        var watchRegistrationKind = request.Provider == AgentProvider.Codex
            ? LidGuardSessionWatchRegistrationKind.CodexShellHostedWorkingDirectoryFallback
            : LidGuardSessionWatchRegistrationKind.WorkingDirectoryFallback;
        return new WatchedProcessResolution(resolvedCandidate.ProcessIdentifier, watchRegistrationKind);
    }

    private string CreateWatcherStatusMessage(LidGuardPipeRequest request, LidGuardSessionSnapshot snapshot)
    {
        if (snapshot.WatchRegistrationKind == LidGuardSessionWatchRegistrationKind.CodexShellHostedWorkingDirectoryFallback) return $" Watching process {snapshot.WatchedProcessIdentifier} through Codex shell-host fallback.";
        if (snapshot.HasWatchedProcess) return $" Watching process {snapshot.WatchedProcessIdentifier}.";

        var shouldExplainSkippedCodexFallback = request.Provider == AgentProvider.Codex
            && request.WatchedProcessIdentifier <= 0
            && _settings.WatchParentProcess;
        if (shouldExplainSkippedCodexFallback) return $" Codex fallback watchdog only attaches when the resolved Codex process or its direct parent is {GetCodexShellHostDescription()}; a stop hook is required.";

        return " No watched process was resolved; a stop hook is required.";
    }

    private static string GetCodexShellHostDescription()
    {
#if LIDGUARD_LINUX
        return "bash, zsh, fish, sh, dash, or pwsh";
#elif LIDGUARD_MACOS
        return "zsh, bash, fish, sh, or pwsh";
#else
        return "cmd.exe, pwsh.exe, or powershell.exe";
#endif
    }

    private LidGuardOperationResult UpdateSettingsInsideGate(LidGuardSettings settings)
    {
        if (!LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(settings.PostStopSuspendSoundVolumeOverridePercent))
        {
            var message =
                $"Post-stop suspend sound volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.";
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
                $"Server runtime cleanup delay minutes must be off or an integer of at least {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}.";
            return LidGuardOperationResult.Failure(message);
        }

        var normalizedSettings = LidGuardSettings.Normalize(settings);
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

    private void StartWatcher(LidGuardSessionSnapshot snapshot)
    {
        CancelWatcher(snapshot.Key);
        if (!_settings.WatchParentProcess) return;
        if (!snapshot.HasWatchedProcess) return;

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
            if (snapshot.IsSoftLocked) continue;

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

        if (_settings.ServerRuntimeCleanupDelayMinutes is not { } serverRuntimeCleanupDelayMinutes)
        {
            RequestServerRuntimeStopInsideGate(deferImmediateStopUntilCurrentResponse);
            return;
        }

        var delay = TimeSpan.FromMinutes(serverRuntimeCleanupDelayMinutes);
        var cancellationTokenSource = new CancellationTokenSource();
        _serverRuntimeCleanupCancellationTokenSource = cancellationTokenSource;
        LidGuardRuntimeLogWriter.AppendRuntimeLog(
            "server-runtime-cleanup-scheduled",
            ServerRuntimeCleanupCommandName,
            CreateSuccessResponse($"Scheduled server runtime cleanup in {serverRuntimeCleanupDelayMinutes} minute(s)."));
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

    private async Task StopServerRuntimeAfterCleanupDelayAsync(
        TimeSpan delay,
        CancellationTokenSource cancellationTokenSource)
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
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

    private void RequestServerRuntimeStopInsideGate(bool deferImmediateStopUntilCurrentResponse)
    {
        _serverRuntimeStopRequested = true;
        LidGuardRuntimeLogWriter.AppendRuntimeLog(
            "server-runtime-cleanup-requested",
            ServerRuntimeCleanupCommandName,
            CreateSuccessResponse("Requested server runtime cleanup because no active sessions remain."));
        if (!deferImmediateStopUntilCurrentResponse) _requestRuntimeStop?.Invoke();
    }

    private async Task WaitForSessionTimeoutAsync(
        TimeSpan delay,
        CancellationTokenSource cancellationTokenSource)
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
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            cancellationTokenSource.Dispose();
        }
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
            .Where(snapshot => !snapshot.IsSoftLocked)
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
            MarkSessionSoftLockedInsideGate(
                SessionTimeoutCommandName,
                "session-timeout-softlock-recorded",
                expiredSnapshot.Provider,
                expiredSnapshot.ProviderName,
                expiredSnapshot.SessionIdentifier,
                $"session-timeout-expired:{sessionTimeoutMinutes} minutes",
                expiredSnapshot.Key);
        }

        ReconfigureSessionTimeoutMonitorInsideGate();
    }

    private static DateTimeOffset AddSessionTimeoutDuration(
        DateTimeOffset lastActivityAt,
        TimeSpan sessionTimeoutDuration)
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
            try
            {
                CleanupWatchedProcessExitInsideGate(snapshot, LidGuardPipeCommands.Stop, "watched-process-exited");
            }
            finally
            {
                _gate.Release();
            }
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
            var alreadyStoppedResponse = CreateSuccessResponse($"Session id {request.SessionIdentifier} is already stopped.");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
            return alreadyStoppedResponse;
        }

        return RemoveSnapshotsInsideGate(
            request,
            matchingSnapshots,
            $"Removed {matchingSnapshots.Length} session(s) matching session id \"{request.SessionIdentifier}\".");
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
            var alreadyStoppedResponse = CreateSuccessResponse(
                $"Session id {request.SessionIdentifier} is already stopped for {AgentProviderDisplay.CreateProviderDisplayText(request.Provider, request.ProviderName)}.");
            LidGuardRuntimeLogWriter.AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
            return alreadyStoppedResponse;
        }

        return RemoveSnapshotsInsideGate(
            request,
            matchingSnapshots,
            $"Removed {matchingSnapshots.Length} session(s) matching {AgentProviderDisplay.CreateProviderDisplayText(request.Provider, request.ProviderName)} session id \"{request.SessionIdentifier}\".");
    }

    private LidGuardPipeResponse RemoveSnapshotsInsideGate(
        LidGuardPipeRequest request,
        LidGuardSessionSnapshot[] matchingSnapshots,
        string multipleRemovalSuccessMessage)
    {
        var lastResponse = CreateSuccessResponse(string.Empty);
        foreach (var matchingSnapshot in matchingSnapshots)
        {
            var stopRequest = new LidGuardSessionStopRequest
            {
                SessionIdentifier = matchingSnapshot.SessionIdentifier,
                Provider = matchingSnapshot.Provider,
                ProviderName = matchingSnapshot.ProviderName
            };
            lastResponse = StopInsideGate(
                stopRequest,
                $"Removed {matchingSnapshot.Key}.",
                "session-removed",
                LidGuardPipeCommands.RemoveSession);
            if (!lastResponse.Succeeded) return lastResponse;
        }

        var successMessage = matchingSnapshots.Length == 1 ? lastResponse.Message : multipleRemovalSuccessMessage;
        if (matchingSnapshots.Length > 1 && TryExtractPostStopScheduleMessage(lastResponse.Message, out var postStopScheduleMessage)) successMessage = $"{successMessage} {postStopScheduleMessage}";
        return CreateSuccessResponse(successMessage);
    }

    private LidGuardPipeResponse StopInsideGate(
        LidGuardSessionStopRequest request,
        string successMessage,
        string eventName = "session-stopped",
        string commandName = LidGuardPipeCommands.Stop)
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var response = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog("session-stop-rejected", request, response, commandName);
            return response;
        }

        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);
        CancelWatcher(key);
        RemoveTranscriptMonitorSession(key);

        if (!_sessionRegistry.Stop(request, out var stoppedSnapshot))
        {
            var response = CreateSuccessResponse($"Session {key} is already stopped.");
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-already-stopped", request, response, commandName);
            return response;
        }

        if (HasSessionsKeepingProtectionAppliedInsideGate())
        {
            ReconfigureSessionTimeoutMonitorInsideGate();
            var response = CreateSuccessResponse(successMessage);
            LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, response, stoppedSnapshot, commandName);
            QueuePostSessionEndWebhookIfRequired(request, stoppedSnapshot, eventName, commandName, _sessionRegistry.ActiveSessionCount);
            return response;
        }

        var restoreResult = RestoreProtection();
        if (!restoreResult.Succeeded)
        {
            ReconfigureSessionTimeoutMonitorInsideGate();
            var failedResponse = CreateFailureResponse(restoreResult);
            LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, failedResponse, stoppedSnapshot, commandName);
            return failedResponse;
        }

        var pendingSuspendContext = CreatePendingSuspendContext(request, stoppedSnapshot, commandName);
        var successResponse = HandleSuspendAfterProtectionReleased(
            pendingSuspendContext,
            stoppedSnapshot,
            eventName,
            successMessage,
            _sessionRegistry.ActiveSessionCount,
            out var suspendScheduled);
        ReconfigureSessionTimeoutMonitorInsideGate();
        if (!suspendScheduled) QueuePostSessionEndWebhookIfRequired(request, stoppedSnapshot, eventName, commandName, _sessionRegistry.ActiveSessionCount);
        ReconfigureServerRuntimeCleanupInsideGate();
        LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, successResponse, stoppedSnapshot, commandName);
        return successResponse;
    }

    private LidGuardPipeResponse HandleSuspendAfterProtectionReleased(
        PendingSuspendContext pendingSuspendContext,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        string successMessage,
        int activeSessionCount,
        out bool suspendScheduled)
    {
        suspendScheduled = false;
        var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("suspend");
        if (!closedLidPolicyApplicability.IsApplicable)
        {
            var response = CreateSuccessResponse(closedLidPolicyApplicability.Message);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-skipped", pendingSuspendContext, response, snapshot);
            return CreateSuccessResponse(successMessage);
        }

        var suspendMode = _settings.SuspendMode;
        var postStopSuspendDelaySeconds = _settings.PostStopSuspendDelaySeconds;
        var scheduledResponse = CreateSuccessResponse(
            $"Scheduled {suspendMode} {DescribePostStopSuspendDelay(postStopSuspendDelaySeconds)} {DescribeSuspendReason(activeSessionCount)}");
        LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-scheduled", pendingSuspendContext, scheduledResponse, snapshot);
        suspendScheduled = true;
        CancelPendingSuspend();
        var pendingSuspendCancellationTokenSource = new CancellationTokenSource();
        _pendingSuspendCancellationTokenSource = pendingSuspendCancellationTokenSource;
        _ = SuspendAfterDelayAsync(
            pendingSuspendContext,
            snapshot,
            eventName,
            CreateSuspendWebhookReason(activeSessionCount),
            activeSessionCount,
            postStopSuspendDelaySeconds,
            pendingSuspendCancellationTokenSource);
        return CreateSuccessResponse(
            $"{successMessage} Scheduled {suspendMode} {DescribePostStopSuspendDelay(postStopSuspendDelaySeconds)} {DescribeSuspendReason(activeSessionCount)}");
    }

    private async Task SuspendAfterDelayAsync(
        PendingSuspendContext pendingSuspendContext,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        SuspendWebhookReason suspendWebhookReason,
        int suspendTriggerSessionCount,
        int postStopSuspendDelaySeconds,
        CancellationTokenSource pendingSuspendCancellationTokenSource)
    {
        try
        {
            await SendPreSuspendWebhookAsync(
                pendingSuspendContext,
                snapshot,
                eventName,
                suspendWebhookReason,
                suspendTriggerSessionCount,
                pendingSuspendCancellationTokenSource.Token);

            if (postStopSuspendDelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(postStopSuspendDelaySeconds), pendingSuspendCancellationTokenSource.Token);

            var postStopSuspendSound = string.Empty;
            int? postStopSuspendSoundVolumeOverridePercent = null;
            await _gate.WaitAsync(pendingSuspendCancellationTokenSource.Token);
            try
            {
                if (HasSessionsKeepingProtectionAppliedInsideGate())
                {
                    var canceledResponse = CreateSuccessResponse("Skipped pending suspend because a session became active before suspend ran.");
                    LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                    return;
                }

                var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("suspend");
                if (!closedLidPolicyApplicability.IsApplicable)
                {
                    var canceledResponse = CreateSuccessResponse(closedLidPolicyApplicability.Message);
                    LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                    return;
                }

                postStopSuspendSound = _settings.PostStopSuspendSound;
                postStopSuspendSoundVolumeOverridePercent = _settings.PostStopSuspendSoundVolumeOverridePercent;
            }
            finally
            {
                _gate.Release();
            }

            await PlayPostStopSuspendSoundAsync(
                pendingSuspendContext,
                snapshot,
                eventName,
                postStopSuspendSound,
                postStopSuspendSoundVolumeOverridePercent,
                pendingSuspendCancellationTokenSource.Token);
            await RequestSuspendAsync(
                pendingSuspendContext,
                snapshot,
                eventName,
                suspendWebhookReason,
                suspendTriggerSessionCount,
                pendingSuspendCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await ClearPendingSuspendAsync(pendingSuspendCancellationTokenSource);
        }
    }

    private async Task RequestSuspendAsync(
        PendingSuspendContext pendingSuspendContext,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        SuspendWebhookReason suspendWebhookReason,
        int suspendTriggerSessionCount,
        CancellationToken cancellationToken)
    {
        var suspendMode = SystemSuspendMode.Sleep;
        int? suspendHistoryEntryCount = null;
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
                var canceledResponse = CreateSuccessResponse(closedLidPolicyApplicability.Message);
                LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                return;
            }

            suspendMode = _settings.SuspendMode;
            suspendHistoryEntryCount = _settings.SuspendHistoryEntryCount;
            activeSessionCount = _sessionRegistry.ActiveSessionCount;
            var requestingResponse = CreateSuccessResponse($"Requesting {suspendMode} {DescribeSuspendReason(activeSessionCount)}");
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-requesting", pendingSuspendContext, requestingResponse, snapshot);
        }
        finally
        {
            _gate.Release();
        }

        var suspendResult = _systemSuspendService.Suspend(suspendMode);
        SuspendHistoryLogStore.Append(
            new SuspendHistoryEntry
            {
                RecordedAt = DateTimeOffset.UtcNow,
                SuspendMode = suspendMode,
                Reason = suspendWebhookReason,
                Succeeded = suspendResult.Succeeded,
                Message = suspendResult.Succeeded
                    ? CreateSuspendHistorySuccessMessage(suspendResult, $"Requested {suspendMode} {DescribeSuspendReason(activeSessionCount)}")
                    : CreateResultMessage(suspendResult),
                EventName = suspendResult.Succeeded ? $"{eventName}-suspend-requested" : $"{eventName}-suspend-failed",
                CommandName = pendingSuspendContext.CommandName,
                Provider = pendingSuspendContext.Provider,
                ProviderName = pendingSuspendContext.ProviderName,
                SessionIdentifier = pendingSuspendContext.SessionIdentifier,
                WorkingDirectory = pendingSuspendContext.WorkingDirectory,
                SessionStateReason = pendingSuspendContext.SessionStateReason,
                ActiveSessionCount = activeSessionCount,
                SuspendTriggerSessionCount = suspendTriggerSessionCount
            },
            suspendHistoryEntryCount);
        if (suspendResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(suspendResult);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-failed", pendingSuspendContext, response, snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CancelWatcher(LidGuardSessionKey key)
    {
        if (!_watcherCancellationTokenSources.Remove(key, out var cancellationTokenSource)) return;

        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }

    private void CancelPendingSuspend()
    {
        if (_pendingSuspendCancellationTokenSource is null) return;

        _pendingSuspendCancellationTokenSource.Cancel();
        _pendingSuspendCancellationTokenSource = null;
    }

    private async Task ClearPendingSuspendAsync(CancellationTokenSource pendingSuspendCancellationTokenSource)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (ReferenceEquals(_pendingSuspendCancellationTokenSource, pendingSuspendCancellationTokenSource)) _pendingSuspendCancellationTokenSource = null;
            ReconfigureServerRuntimeCleanupInsideGate(false);
        }
        finally
        {
            _gate.Release();
        }

        pendingSuspendCancellationTokenSource.Dispose();
    }

    private async Task PlayPostStopSuspendSoundAsync(
        PendingSuspendContext pendingSuspendContext,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        string postStopSuspendSound,
        int? postStopSuspendSoundVolumeOverridePercent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(postStopSuspendSound)) return;

        var playbackResult = await _postStopSuspendSoundPlaybackCoordinator.PlayAsync(
            postStopSuspendSound,
            postStopSuspendSoundVolumeOverridePercent,
            cancellationToken);
        foreach (var volumeWarningResult in playbackResult.VolumeWarningResults)
        {
            await AppendPostStopSuspendSoundVolumeWarningAsync(
                pendingSuspendContext,
                snapshot,
                eventName,
                volumeWarningResult);
        }

        if (playbackResult.PlaybackResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(playbackResult.PlaybackResult);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-sound-failed", pendingSuspendContext, response, snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AppendPostStopSuspendSoundVolumeWarningAsync(
        PendingSuspendContext pendingSuspendContext,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        LidGuardOperationResult volumeWarningResult)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateSuccessResponse($"Warning: {CreateResultMessage(volumeWarningResult)}");
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-sound-volume-warning", pendingSuspendContext, response, snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SendPreSuspendWebhookAsync(
        PendingSuspendContext pendingSuspendContext,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        SuspendWebhookReason suspendWebhookReason,
        int suspendTriggerSessionCount,
        CancellationToken cancellationToken)
    {
        string preSuspendWebhookUrl;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            preSuspendWebhookUrl = _settings.PreSuspendWebhookUrl;
        }
        finally
        {
            _gate.Release();
        }

        var sendResult = await SuspendWebhookSender.SendAsync(
            preSuspendWebhookUrl,
            suspendWebhookReason,
            suspendTriggerSessionCount,
            cancellationToken,
            s_preSuspendWebhookTimeout);
        if (sendResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(sendResult);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-suspend-webhook-failed", pendingSuspendContext, response, snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void QueuePostSessionEndWebhookIfRequired(
        LidGuardSessionStopRequest request,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        string commandName,
        int activeSessionCount)
    {
        if (!request.IsProviderSessionEnd) return;
        if (string.IsNullOrWhiteSpace(_settings.PostSessionEndWebhookUrl)) return;

        var postSessionEndWebhookUrl = _settings.PostSessionEndWebhookUrl;
        var webhookRequest = new LidGuardWebhookRequest
        {
            EventType = LidGuardWebhookEventTypes.PostSessionEnd,
            Reason = LidGuardWebhookReasons.SessionEnded,
            Provider = snapshot.Provider.ToString(),
            ProviderName = string.IsNullOrWhiteSpace(snapshot.ProviderName) ? null : snapshot.ProviderName,
            SessionIdentifier = snapshot.SessionIdentifier,
            StartedAtUtc = snapshot.StartedAt,
            LastActivityAtUtc = snapshot.LastActivityAt,
            EndedAtUtc = DateTimeOffset.UtcNow,
            EndReason = string.IsNullOrWhiteSpace(request.SessionEndReason) ? commandName : request.SessionEndReason,
            ActiveSessionCount = activeSessionCount,
            WorkingDirectory = string.IsNullOrWhiteSpace(snapshot.WorkingDirectory) ? null : snapshot.WorkingDirectory,
            TranscriptPath = string.IsNullOrWhiteSpace(snapshot.TranscriptPath) ? null : snapshot.TranscriptPath
        };

        _pendingPostSessionEndWebhookCount++;
        _ = SendPostSessionEndWebhookAsync(
            postSessionEndWebhookUrl,
            webhookRequest,
            request,
            snapshot,
            eventName,
            commandName,
            activeSessionCount);
    }

    private async Task SendPostSessionEndWebhookAsync(
        string postSessionEndWebhookUrl,
        LidGuardWebhookRequest webhookRequest,
        LidGuardSessionStopRequest request,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        string commandName,
        int activeSessionCount)
    {
        try
        {
            LidGuardOperationResult sendResult;
            try
            {
                sendResult = await SuspendWebhookSender.SendPostSessionEndAsync(
                    postSessionEndWebhookUrl,
                    webhookRequest,
                    CancellationToken.None,
                    s_postSessionEndWebhookTimeout);
            }
            catch (Exception exception)
            {
                sendResult = LidGuardOperationResult.Failure($"Failed to send the post-session-end webhook: {exception.Message}");
            }

            if (sendResult.Succeeded) return;

            var response = LidGuardPipeResponse.Failure(sendResult.Message, activeSessionCount);
            LidGuardRuntimeLogWriter.AppendSessionLog($"{eventName}-post-session-end-webhook-failed", request, response, snapshot, commandName);
        }
        finally
        {
            await CompletePostSessionEndWebhookAsync();
        }
    }

    private async Task CompletePostSessionEndWebhookAsync()
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_pendingPostSessionEndWebhookCount > 0) _pendingPostSessionEndWebhookCount--;
            ReconfigureServerRuntimeCleanupInsideGate(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HandleEmergencyHibernationThresholdReachedAsync(
        EmergencyHibernationThermalThresholdReachedContext emergencyHibernationThermalThresholdReachedContext)
    {
        var emergencyHibernationTemperatureCelsius = emergencyHibernationThermalThresholdReachedContext.ThresholdTemperatureCelsius;
        var emergencyHibernationTemperatureMode = emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureMode;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (!TryValidateEmergencyHibernationStateInsideGate(
                emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
                out emergencyHibernationTemperatureCelsius,
                out emergencyHibernationTemperatureMode,
                out var canceledMessage))
            {
                LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog(
                    "emergency-hibernation-canceled",
                    CreateSuccessResponse(canceledMessage),
                    emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
                    emergencyHibernationTemperatureCelsius,
                    emergencyHibernationTemperatureMode);
                return;
            }

            CancelPendingSuspend();
            LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog(
                "emergency-hibernation-thermal-detected",
                CreateSuccessResponse(
                    $"Detected high system temperature {DescribeEmergencyHibernationTemperature(emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode)}. Requesting Emergency Hibernation."),
                emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius,
                emergencyHibernationTemperatureMode);
        }
        finally
        {
            _gate.Release();
        }

        await SendEmergencyHibernationWebhookAsync(
            emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
            emergencyHibernationTemperatureCelsius,
            emergencyHibernationTemperatureMode);
        await RequestEmergencyHibernationAsync(
            emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
            emergencyHibernationTemperatureCelsius,
            emergencyHibernationTemperatureMode);
    }

    private async Task SendEmergencyHibernationWebhookAsync(
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        string preSuspendWebhookUrl;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            preSuspendWebhookUrl = _settings.PreSuspendWebhookUrl;
        }
        finally
        {
            _gate.Release();
        }

        var sendResult = await SuspendWebhookSender.SendAsync(
            preSuspendWebhookUrl,
            SuspendWebhookReason.EmergencyHibernation,
            0,
            CancellationToken.None,
            s_emergencyHibernationWebhookTimeout);
        if (sendResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog(
                "emergency-hibernation-webhook-failed",
                CreateFailureResponse(sendResult),
                observedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius,
                emergencyHibernationTemperatureMode);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RequestEmergencyHibernationAsync(
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        int? suspendHistoryEntryCount = null;
        var activeSessionCount = 0;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (!TryValidateEmergencyHibernationStateInsideGate(
                observedTemperatureCelsius,
                out emergencyHibernationTemperatureCelsius,
                out emergencyHibernationTemperatureMode,
                out var canceledMessage))
            {
                LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog(
                    "emergency-hibernation-canceled",
                    CreateSuccessResponse(canceledMessage),
                    observedTemperatureCelsius,
                    emergencyHibernationTemperatureCelsius,
                    emergencyHibernationTemperatureMode);
                return;
            }

            suspendHistoryEntryCount = _settings.SuspendHistoryEntryCount;
            activeSessionCount = _sessionRegistry.ActiveSessionCount;
            LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog(
                "emergency-hibernation-requesting",
                CreateSuccessResponse(
                    $"Requesting Emergency Hibernation because system temperature reached {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode)}."),
                observedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius,
                emergencyHibernationTemperatureMode);
        }
        finally
        {
            _gate.Release();
        }

        var hibernationResult = _systemSuspendService.Suspend(SystemSuspendMode.Hibernate);
        SuspendHistoryLogStore.Append(
            new SuspendHistoryEntry
            {
                RecordedAt = DateTimeOffset.UtcNow,
                SuspendMode = SystemSuspendMode.Hibernate,
                Reason = SuspendWebhookReason.EmergencyHibernation,
                Succeeded = hibernationResult.Succeeded,
                Message = hibernationResult.Succeeded
                    ? CreateSuspendHistorySuccessMessage(
                        hibernationResult,
                        $"Requested Emergency Hibernation because system temperature reached {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode)}.")
                    : CreateResultMessage(hibernationResult),
                EventName = hibernationResult.Succeeded ? "emergency-hibernation-requested" : "emergency-hibernation-failed",
                CommandName = "emergency-hibernation-monitor",
                ActiveSessionCount = activeSessionCount,
                ObservedTemperatureCelsius = observedTemperatureCelsius,
                EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius,
                EmergencyHibernationTemperatureMode = emergencyHibernationTemperatureMode
            },
            suspendHistoryEntryCount);
        if (hibernationResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            LidGuardRuntimeLogWriter.AppendEmergencyHibernationLog(
                "emergency-hibernation-failed",
                CreateFailureResponse(hibernationResult),
                observedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius,
                emergencyHibernationTemperatureMode);
        }
        finally
        {
            _gate.Release();
        }
    }

    private EmergencyHibernationThermalMonitorState CreateEmergencyHibernationThermalMonitorState()
    {
        var closedLidPolicyApplicability = EvaluateClosedLidPolicyApplicability("Emergency Hibernation");
        return new EmergencyHibernationThermalMonitorState(
            _protectionCoordinator.IsApplied,
            _settings.EmergencyHibernationOnHighTemperature,
            closedLidPolicyApplicability.IsApplicable,
            closedLidPolicyApplicability.LidSwitchState,
            closedLidPolicyApplicability.VisibleDisplayMonitorCount,
            _settings.EmergencyHibernationTemperatureMode,
            LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius(_settings.EmergencyHibernationTemperatureCelsius));
    }

    private bool TryValidateEmergencyHibernationStateInsideGate(
        int observedTemperatureCelsius,
        out int emergencyHibernationTemperatureCelsius,
        out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode,
        out string message)
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

    private LidGuardPipeResponse CreateSuccessResponse(string message)
    {
        var snapshots = _sessionRegistry.GetSnapshots();
        var currentLidAndDisplayState = GetCurrentLidAndDisplayState();
        return LidGuardPipeResponse.Success(
            message,
            snapshots.Count,
            CreateSessionStatuses(snapshots),
            _settings,
            currentLidAndDisplayState.LidSwitchState,
            currentLidAndDisplayState.VisibleDisplayMonitorCount);
    }

    private LidGuardPipeResponse CreateFailureResponse(LidGuardOperationResult result)
    {
        var snapshots = _sessionRegistry.GetSnapshots();
        var currentLidAndDisplayState = GetCurrentLidAndDisplayState();
        return LidGuardPipeResponse.Failure(
            CreateResultMessage(result),
            snapshots.Count,
            false,
            currentLidAndDisplayState.LidSwitchState,
            currentLidAndDisplayState.VisibleDisplayMonitorCount);
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

    private static string CreateSuspendHistorySuccessMessage(LidGuardOperationResult result, string defaultMessage)
        => string.IsNullOrWhiteSpace(result.Message) ? defaultMessage : $"{defaultMessage} {result.Message}";

    private ClosedLidPolicyApplicability EvaluateClosedLidPolicyApplicability(string actionName)
    {
        var lidSwitchState = _lidStateSource.CurrentState;
        var visibleDisplayMonitorCount = _visibleDisplayMonitorCountProvider.GetVisibleDisplayMonitorCount(
            excludeInternalDisplayMonitors: lidSwitchState == LidSwitchState.Closed);
        return EvaluateClosedLidPolicyApplicability(actionName, new CurrentLidAndDisplayState(lidSwitchState, visibleDisplayMonitorCount));
    }

    private CurrentLidAndDisplayState GetCurrentLidAndDisplayState()
    {
        var lidSwitchState = _lidStateSource.CurrentState;
        return new(lidSwitchState, _visibleDisplayMonitorCountProvider.GetVisibleDisplayMonitorCount());
    }

    private static ClosedLidPolicyApplicability EvaluateClosedLidPolicyApplicability(
        string actionName,
        CurrentLidAndDisplayState currentLidAndDisplayState)
    {
        if (currentLidAndDisplayState.LidSwitchState == LidSwitchState.Open)
        {
            return new ClosedLidPolicyApplicability(
                false,
                currentLidAndDisplayState.LidSwitchState,
                currentLidAndDisplayState.VisibleDisplayMonitorCount,
                $"Skipped {actionName} because the lid is open.");
        }

        if (currentLidAndDisplayState.LidSwitchState != LidSwitchState.Closed)
        {
            return new ClosedLidPolicyApplicability(
                false,
                currentLidAndDisplayState.LidSwitchState,
                currentLidAndDisplayState.VisibleDisplayMonitorCount,
                $"Skipped {actionName} because the lid state is {currentLidAndDisplayState.LidSwitchState}.");
        }

        if (currentLidAndDisplayState.VisibleDisplayMonitorCount > 0)
        {
            return new ClosedLidPolicyApplicability(
                false,
                currentLidAndDisplayState.LidSwitchState,
                currentLidAndDisplayState.VisibleDisplayMonitorCount,
                $"Skipped {actionName} because {currentLidAndDisplayState.VisibleDisplayMonitorCount} visible display monitor(s) are active while the lid is closed.");
        }

        return new ClosedLidPolicyApplicability(
            true,
            currentLidAndDisplayState.LidSwitchState,
            currentLidAndDisplayState.VisibleDisplayMonitorCount,
            string.Empty);
    }

    private static string DescribePostStopSuspendDelay(int postStopSuspendDelaySeconds)
        => postStopSuspendDelaySeconds == 0 ? "immediately" : $"in {postStopSuspendDelaySeconds} second(s)";

    private static SuspendWebhookReason CreateSuspendWebhookReason(int activeSessionCount)
        => activeSessionCount == 0 ? SuspendWebhookReason.Completed : SuspendWebhookReason.SoftLocked;

    private static string DescribeSuspendReason(int activeSessionCount)
        => activeSessionCount == 0
            ? "because the lid is closed, no suspend-blocking visible display monitors remain, and the last session stopped."
            : "because the lid is closed, no suspend-blocking visible display monitors remain, and all remaining sessions are soft-locked.";

    private static string DescribeEmergencyHibernationTemperature(
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
        => $"{observedTemperatureCelsius} Celsius using {emergencyHibernationTemperatureMode} mode (threshold: {emergencyHibernationTemperatureCelsius} Celsius)";

    private CleanupResult CleanupWatchedProcessExitInsideGate(LidGuardSessionSnapshot snapshot, string commandName, string eventName)
    {
        if (!LidGuardWatchedProcessCleanup.ShouldCleanCodexWorkingDirectory(snapshot))
        {
            var successMessage = eventName == "watched-process-exited"
                ? $"Watched process exited for {snapshot.Key}."
                : $"Cleaned orphan session {snapshot.Key}.";
            var stopResponse = StopInsideGate(
                new LidGuardSessionStopRequest
                {
                    SessionIdentifier = snapshot.SessionIdentifier,
                    Provider = snapshot.Provider,
                    ProviderName = snapshot.ProviderName
                },
                successMessage,
                eventName,
                commandName);
            var removedSessionCount = stopResponse.Succeeded && !stopResponse.Message.Contains("already stopped", StringComparison.Ordinal) ? 1 : 0;
            return new CleanupResult(stopResponse, removedSessionCount);
        }

        var matchingSnapshots = _sessionRegistry
            .GetSnapshots()
            .Where(activeSnapshot => activeSnapshot.Provider == AgentProvider.Codex)
            .Where(activeSnapshot => activeSnapshot.HasWatchedProcess)
            .Where(activeSnapshot => LidGuardWatchedProcessCleanup.WorkingDirectoriesMatch(activeSnapshot.WorkingDirectory, snapshot.WorkingDirectory))
            .ToArray();
        if (matchingSnapshots.Length == 0)
        {
            var alreadyStoppedResponse = CreateSuccessResponse($"Watched Codex working directory \"{snapshot.WorkingDirectory}\" is already stopped.");
            return new CleanupResult(alreadyStoppedResponse, 0);
        }

        var lastResponse = CreateSuccessResponse(string.Empty);
        foreach (var matchingSnapshot in matchingSnapshots)
        {
            var stopRequest = new LidGuardSessionStopRequest
            {
                SessionIdentifier = matchingSnapshot.SessionIdentifier,
                Provider = matchingSnapshot.Provider,
                ProviderName = matchingSnapshot.ProviderName
            };
            var successMessage = eventName == "watched-process-exited"
                ? $"Watched process exited for {matchingSnapshot.Key}."
                : $"Cleaned watched Codex session {matchingSnapshot.Key} for working directory \"{matchingSnapshot.WorkingDirectory}\".";
            lastResponse = StopInsideGate(
                stopRequest,
                successMessage,
                eventName,
                commandName);
            if (!lastResponse.Succeeded) return new CleanupResult(lastResponse, 0);
        }

        var finalSuccessMessage =
            $"Cleaned {matchingSnapshots.Length} watched Codex session(s) for working directory \"{snapshot.WorkingDirectory}\" and left process=none Codex sessions untouched.";
        if (TryExtractPostStopScheduleMessage(lastResponse.Message, out var postStopScheduleMessage)) finalSuccessMessage = $"{finalSuccessMessage} {postStopScheduleMessage}";

        var successResponse = CreateSuccessResponse(finalSuccessMessage);
        return new CleanupResult(successResponse, matchingSnapshots.Length);
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

        var successMessage = changed
            ? $"Cleared soft-lock for {key} because activity was detected from {request.SessionStateReason}."
            : $"Session {key} was already active.";
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

    private void AppendTranscriptMonitorRegistration(
        LidGuardPipeRequest request,
        LidGuardSessionSnapshot snapshot,
        AgentTranscriptMonitoringRegistrationResult transcriptMonitoringRegistrationResult)
    {
        if (!IsTranscriptMonitoringProvider(request.Provider)) return;
        if (string.IsNullOrWhiteSpace(transcriptMonitoringRegistrationResult.Message)) return;

        var response = CreateSuccessResponse(transcriptMonitoringRegistrationResult.Message);
        var eventNamePrefix = GetTranscriptMonitorEventNamePrefix(request.Provider);
        var eventName = transcriptMonitoringRegistrationResult.MonitoringEnabled
            ? $"{eventNamePrefix}-transcript-monitor-configured"
            : $"{eventNamePrefix}-transcript-monitor-skipped";
        LidGuardRuntimeLogWriter.AppendSessionLog(eventName, request, response, snapshot);
    }

    private static bool IsTranscriptMonitoringProvider(AgentProvider provider)
        => provider is AgentProvider.Codex or AgentProvider.Claude or AgentProvider.GitHubCopilot;

    private static string GetTranscriptMonitorEventNamePrefix(AgentProvider provider)
        => provider == AgentProvider.GitHubCopilot ? "github-copilot" : provider.ToString().ToLowerInvariant();

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
        try
        {
            MarkSessionActiveInsideGate(request);
        }
        finally
        {
            _gate.Release();
        }
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
        try
        {
            StopInsideGate(
                request,
                $"Stopped {transcriptStopDetectedContext.SessionKey} because {transcriptStopDetectedContext.StopReasonDescription}.",
                transcriptStopDetectedContext.StopCommandName,
                transcriptStopDetectedContext.StopCommandName);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HandleTranscriptSoftLockDetectedAsync(AgentTranscriptSoftLockDetectedContext transcriptSoftLockDetectedContext)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            MarkSessionSoftLockedInsideGate(
                transcriptSoftLockDetectedContext.SoftLockCommandName,
                transcriptSoftLockDetectedContext.SoftLockEventName,
                transcriptSoftLockDetectedContext.SessionKey.Provider,
                transcriptSoftLockDetectedContext.SessionKey.ProviderName,
                transcriptSoftLockDetectedContext.SessionKey.SessionIdentifier,
                transcriptSoftLockDetectedContext.SoftLockReason,
                transcriptSoftLockDetectedContext.SessionKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AgentTranscriptMonitoringProfile CreateCodexTranscriptMonitoringProfile()
        => new()
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

    private static AgentTranscriptMonitoringProfile CreateClaudeTranscriptMonitoringProfile()
        => new()
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

    private static AgentTranscriptMonitoringProfile CreateGitHubCopilotTranscriptMonitoringProfile()
        => new()
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

    private static PendingSuspendContext CreatePendingSuspendContext(
        LidGuardPipeRequest request,
        LidGuardSessionSnapshot snapshot)
        => new(
            request.Provider,
            AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName),
            request.SessionIdentifier,
            snapshot.WorkingDirectory,
            request.Command,
            request.SessionStateReason);

    private static PendingSuspendContext CreatePendingSuspendContext(
        LidGuardSessionStopRequest request,
        LidGuardSessionSnapshot snapshot,
        string commandName)
        => new(
            request.Provider,
            AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName),
            request.SessionIdentifier,
            snapshot.WorkingDirectory,
            commandName,
            string.Empty);

    private readonly record struct CleanupResult(LidGuardPipeResponse Response, int RemovedSessionCount);

    private readonly record struct ClosedLidPolicyApplicability(
        bool IsApplicable,
        LidSwitchState LidSwitchState,
        int VisibleDisplayMonitorCount,
        string Message);

    private readonly record struct CurrentLidAndDisplayState(
        LidSwitchState LidSwitchState,
        int VisibleDisplayMonitorCount);

    private readonly record struct WatchedProcessResolution(
        int ProcessIdentifier,
        LidGuardSessionWatchRegistrationKind WatchRegistrationKind)
    {
        public static WatchedProcessResolution None { get; } = new(0, LidGuardSessionWatchRegistrationKind.None);
    }
}


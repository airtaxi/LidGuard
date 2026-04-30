using System.Diagnostics;
using LidGuard.Ipc;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Runtime;

internal sealed class LidGuardRuntimeCoordinator
{
    private static readonly TimeSpan s_processWatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_emergencyHibernationWebhookTimeout = TimeSpan.FromSeconds(5);
    private const string EmergencyHibernationMonitorCommandName = "emergency-hibernation-monitor";

    private readonly IPowerRequestService _powerRequestService;
    private readonly ICommandLineProcessResolver _commandLineProcessResolver;
    private readonly IProcessExitWatcher _processExitWatcher;
    private readonly IPostStopSuspendSoundPlayer _postStopSuspendSoundPlayer;
    private readonly ISystemSuspendService _systemSuspendService;
    private readonly ILidStateSource _lidStateSource;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LidGuardSessionRegistry _sessionRegistry = new();
    private readonly Dictionary<LidGuardSessionKey, CancellationTokenSource> _watcherCancellationTokenSources = [];
    private readonly LidGuardPendingLidActionBackupManager _pendingLidActionBackupManager;
    private readonly CodexSoftLockTranscriptMonitor _codexSoftLockTranscriptMonitor;
    private readonly EmergencyHibernationThermalMonitor _emergencyHibernationThermalMonitor;

    private LidGuardSettings _settings;
    private ILidGuardPowerRequest _powerRequest = InactiveLidGuardPowerRequest.Instance;
    private LidActionBackup _lidActionBackup;
    private bool _hasLidActionBackup;
    private bool _protectionApplied;
    private CancellationTokenSource _pendingSuspendCancellationTokenSource;

    public LidGuardRuntimeCoordinator(
        LidGuardSettings initialSettings,
        IPowerRequestService powerRequestService,
        ICommandLineProcessResolver commandLineProcessResolver,
        IProcessExitWatcher processExitWatcher,
        LidActionPolicyController lidActionPolicyController,
        ISystemSuspendService systemSuspendService,
        IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer,
        ILidStateSource lidStateSource)
    {
        _powerRequestService = powerRequestService;
        _commandLineProcessResolver = commandLineProcessResolver;
        _processExitWatcher = processExitWatcher;
        _postStopSuspendSoundPlayer = postStopSuspendSoundPlayer;
        _systemSuspendService = systemSuspendService;
        _lidStateSource = lidStateSource;
        _pendingLidActionBackupManager = new LidGuardPendingLidActionBackupManager(lidActionPolicyController);
        _codexSoftLockTranscriptMonitor = new CodexSoftLockTranscriptMonitor(HandleCodexTranscriptActivityThresholdReachedAsync);
        _emergencyHibernationThermalMonitor = new EmergencyHibernationThermalMonitor(
            CreateEmergencyHibernationThermalMonitorState,
            HandleEmergencyHibernationThresholdReachedAsync);
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

    private async Task<LidGuardPipeResponse> StartAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var response = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            AppendSessionLog("session-start-rejected", request, response);
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
                    AppendSessionLog("session-start-failed", request, response);
                    return response;
                }
            }

            var codexTranscriptMonitoringRegistrationResult = request.Provider == AgentProvider.Codex
                ? _codexSoftLockTranscriptMonitor.RegisterOrUpdateSession(request.SessionIdentifier, request.WorkingDirectory, request.TranscriptPath)
                : new CodexTranscriptMonitoringRegistrationResult();
            var watchedProcessIdentifier = ResolveWatchedProcessIdentifier(request);
            var startRequest = new LidGuardSessionStartRequest
            {
                SessionIdentifier = request.SessionIdentifier,
                Provider = request.Provider,
                ProviderName = request.ProviderName,
                StartedAt = DateTimeOffset.UtcNow,
                WatchedProcessIdentifier = watchedProcessIdentifier,
                WorkingDirectory = request.WorkingDirectory,
                TranscriptPath = codexTranscriptMonitoringRegistrationResult.ResolvedTranscriptPath
            };

            var snapshot = _sessionRegistry.StartOrUpdate(startRequest);
            var protectionResult = EnsureProtection();
            if (!protectionResult.Succeeded)
            {
                _codexSoftLockTranscriptMonitor.RemoveSession(snapshot.Key);
                _sessionRegistry.Stop(
                    new LidGuardSessionStopRequest
                    {
                        SessionIdentifier = request.SessionIdentifier,
                        Provider = request.Provider,
                        ProviderName = request.ProviderName
                    },
                    out _);
                var response = CreateFailureResponse(protectionResult);
                AppendSessionLog("session-start-failed", request, response);
                return response;
            }

            CancelPendingSuspend();
            StartWatcher(snapshot);
            AppendCodexTranscriptMonitorRegistration(request, snapshot, codexTranscriptMonitoringRegistrationResult);

            var watchMessage = CreateWatcherStatusMessage(request, snapshot);
            var successResponse = CreateSuccessResponse($"Started {snapshot.Key}.{watchMessage}");
            AppendSessionLog("session-started", request, successResponse, snapshot);
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
            AppendSessionLog("settings-update-rejected", request, response);
            return response;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settingsResult = UpdateSettingsInsideGate(request.Settings);
            if (!settingsResult.Succeeded)
            {
                var response = CreateFailureResponse(settingsResult);
                AppendSessionLog("settings-update-failed", request, response);
                return response;
            }

            var successResponse = CreateSuccessResponse("Updated LidGuard runtime settings.");
            AppendSessionLog("settings-updated", request, successResponse);
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
                ProviderName = request.ProviderName
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
                AppendSessionLog("session-softlock-rejected", request, rejectedResponse);
                return rejectedResponse;
            }

            var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);
            if (!_sessionRegistry.TryMarkSoftLocked(
                request.Provider,
                request.SessionIdentifier,
                request.ProviderName,
                request.SessionStateReason,
                DateTimeOffset.UtcNow,
                out var snapshot,
                out var changed))
            {
                var ignoredResponse = CreateSuccessResponse($"Session {key} is not active; ignored soft-lock signal.");
                AppendSessionLog("session-softlock-ignored", request, ignoredResponse);
                return ignoredResponse;
            }

            _codexSoftLockTranscriptMonitor.ArmSessionSoftLock(key);
            var successMessage = changed
                ? $"Marked {key} as soft-locked from {request.SessionStateReason}."
                : $"Session {key} is already soft-locked from {snapshot.SoftLockReason}.";
            if (HasSessionsKeepingProtectionAppliedInsideGate())
            {
                var successResponse = CreateSuccessResponse(successMessage);
                AppendSessionLog("session-softlock-recorded", request, successResponse, snapshot);
                return successResponse;
            }

            var restoreResult = RestoreProtection();
            if (!restoreResult.Succeeded)
            {
                var failedResponse = CreateFailureResponse(restoreResult);
                AppendSessionLog("session-softlock-failed", request, failedResponse, snapshot);
                return failedResponse;
            }

            var pendingSuspendContext = CreatePendingSuspendContext(request, snapshot);
            var successResponseWithSuspendPlan = HandleSuspendAfterProtectionReleased(
                pendingSuspendContext,
                snapshot,
                "session-softlock-recorded",
                successMessage,
                _sessionRegistry.ActiveSessionCount);
            AppendSessionLog("session-softlock-recorded", request, successResponseWithSuspendPlan, snapshot);
            return successResponseWithSuspendPlan;
        }
        finally
        {
            _gate.Release();
        }
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
            AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
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
            foreach (var snapshot in _sessionRegistry.GetSnapshots())
            {
                if (!snapshot.HasWatchedProcess) continue;
                if (IsProcessRunning(snapshot.WatchedProcessIdentifier)) continue;

                var stopResponse = StopInsideGate(
                    new LidGuardSessionStopRequest
                    {
                        SessionIdentifier = snapshot.SessionIdentifier,
                        Provider = snapshot.Provider,
                        ProviderName = snapshot.ProviderName
                    },
                    $"Cleaned orphan session {snapshot.Key}.",
                    "orphan-session-cleaned",
                    LidGuardPipeCommands.CleanupOrphans);

                if (!stopResponse.Succeeded) cleanupFailureMessages.Add(stopResponse.Message);
                cleanupCount++;
            }

            if (cleanupFailureMessages.Count > 0)
            {
                var response = LidGuardPipeResponse.Failure(string.Join(" ", cleanupFailureMessages), _sessionRegistry.ActiveSessionCount);
                AppendRuntimeLog("cleanup-orphans-failed", LidGuardPipeCommands.CleanupOrphans, response);
                return response;
            }

            var successResponse = CreateSuccessResponse($"Cleaned {cleanupCount} orphan session(s).");
            AppendRuntimeLog("cleanup-orphans-completed", LidGuardPipeCommands.CleanupOrphans, successResponse);
            return successResponse;
        }
        finally
        {
            _gate.Release();
        }
    }

    private int ResolveWatchedProcessIdentifier(LidGuardPipeRequest request)
    {
        if (request.WatchedProcessIdentifier > 0) return request.WatchedProcessIdentifier;
        if (request.Provider == AgentProvider.Mcp) return 0;
        if (!_settings.WatchParentProcess) return 0;
        if (ShouldSkipImplicitCodexWatchdog(request)) return 0;
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory)) return 0;

        var resolveResult = _commandLineProcessResolver.FindForWorkingDirectory(request.WorkingDirectory, request.Provider);
        return resolveResult.Succeeded ? resolveResult.Value.ProcessIdentifier : 0;
    }

    private string CreateWatcherStatusMessage(LidGuardPipeRequest request, LidGuardSessionSnapshot snapshot)
    {
        if (snapshot.HasWatchedProcess) return $" Watching process {snapshot.WatchedProcessIdentifier}.";

        var shouldExplainSkippedCodexFallback = _settings.WatchParentProcess && ShouldSkipImplicitCodexWatchdog(request);
        if (shouldExplainSkippedCodexFallback) return " Codex fallback watchdog is disabled without an explicit parent process id because Codex App helper sessions can bind the wrong process; a stop hook is required.";

        return " No watched process was resolved; a stop hook is required.";
    }

    private static bool ShouldSkipImplicitCodexWatchdog(LidGuardPipeRequest request)
        => request.Provider == AgentProvider.Codex && request.WatchedProcessIdentifier <= 0;

    private LidGuardOperationResult UpdateSettingsInsideGate(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (!_sessionRegistry.HasActiveSessions)
        {
            _settings = normalizedSettings;
            ReconfigureWatchers();
            EnsureEmergencyHibernationThermalMonitor();
            return LidGuardOperationResult.Success();
        }

        var previousSettings = _settings;
        var restoreResult = RestoreProtection();
        if (!restoreResult.Succeeded) return restoreResult;

        _settings = normalizedSettings;
        if (!HasSessionsKeepingProtectionAppliedInsideGate())
        {
            ReconfigureWatchers();
            EnsureEmergencyHibernationThermalMonitor();
            return LidGuardOperationResult.Success();
        }

        var protectionResult = EnsureProtection();
        if (protectionResult.Succeeded)
        {
            ReconfigureWatchers();
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
        if (_protectionApplied) return LidGuardOperationResult.Success();

        var powerRequestResult = _powerRequestService.Create(_settings.PowerRequest);
        if (!powerRequestResult.Succeeded) return LidGuardOperationResult.Failure(powerRequestResult.Message, powerRequestResult.NativeErrorCode);

        _powerRequest = powerRequestResult.Value;

        if (_settings.ChangeLidAction)
        {
            var lidActionResult = _pendingLidActionBackupManager.ApplyTemporaryDoNothing(_settings);
            if (!lidActionResult.Succeeded)
            {
                RestoreProtection();
                return LidGuardOperationResult.Failure(lidActionResult.Message, lidActionResult.NativeErrorCode);
            }

            _lidActionBackup = lidActionResult.Value;
            _hasLidActionBackup = true;
        }

        _protectionApplied = true;
        EnsureEmergencyHibernationThermalMonitor();
        return LidGuardOperationResult.Success();
    }

    private LidGuardOperationResult RestoreProtection()
    {
        var restoreMessages = new List<string>();

        if (_hasLidActionBackup)
        {
            var restoreResult = _pendingLidActionBackupManager.Restore(_lidActionBackup);
            if (!restoreResult.Succeeded) restoreMessages.Add(CreateResultMessage(restoreResult));
            _hasLidActionBackup = false;
        }

        DisposePowerRequest();
        _protectionApplied = false;
        CancelEmergencyHibernationThermalMonitor();

        return restoreMessages.Count == 0
            ? LidGuardOperationResult.Success()
            : LidGuardOperationResult.Failure(string.Join(" ", restoreMessages));
    }

    private void DisposePowerRequest()
    {
        _powerRequest.Dispose();
        _powerRequest = InactiveLidGuardPowerRequest.Instance;
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

    private void EnsureEmergencyHibernationThermalMonitor()
    {
        if (!_protectionApplied || !_settings.EmergencyHibernationOnHighTemperature)
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
                StopInsideGate(
                    new LidGuardSessionStopRequest
                    {
                        SessionIdentifier = snapshot.SessionIdentifier,
                        Provider = snapshot.Provider,
                        ProviderName = snapshot.ProviderName
                    },
                    $"Watched process exited for {snapshot.Key}.",
                    "watched-process-exited");
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
            AppendSessionLog("session-remove-rejected", request, rejectedResponse);
            return rejectedResponse;
        }

        var matchingSnapshots = _sessionRegistry
            .GetSnapshots()
            .Where(snapshot => string.Equals(snapshot.SessionIdentifier, request.SessionIdentifier, StringComparison.Ordinal))
            .ToArray();
        if (matchingSnapshots.Length == 0)
        {
            var alreadyStoppedResponse = CreateSuccessResponse($"Session id {request.SessionIdentifier} is already stopped.");
            AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
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
            AppendSessionLog("session-remove-rejected", request, rejectedResponse);
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
            AppendSessionLog("session-remove-already-stopped", request, alreadyStoppedResponse);
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
        if (matchingSnapshots.Length > 1 && TryExtractPostStopScheduleMessage(lastResponse.Message, out var postStopScheduleMessage))
            successMessage = $"{successMessage} {postStopScheduleMessage}";
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
            AppendSessionLog("session-stop-rejected", request, response, commandName);
            return response;
        }

        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);
        CancelWatcher(key);
        _codexSoftLockTranscriptMonitor.RemoveSession(key);

        if (!_sessionRegistry.Stop(request, out var stoppedSnapshot))
        {
            var response = CreateSuccessResponse($"Session {key} is already stopped.");
            AppendSessionLog($"{eventName}-already-stopped", request, response, commandName);
            return response;
        }

        if (HasSessionsKeepingProtectionAppliedInsideGate())
        {
            var response = CreateSuccessResponse(successMessage);
            AppendSessionLog(eventName, request, response, stoppedSnapshot, commandName);
            return response;
        }

        var restoreResult = RestoreProtection();
        if (!restoreResult.Succeeded)
        {
            var failedResponse = CreateFailureResponse(restoreResult);
            AppendSessionLog(eventName, request, failedResponse, stoppedSnapshot, commandName);
            return failedResponse;
        }

        var pendingSuspendContext = CreatePendingSuspendContext(request, stoppedSnapshot, commandName);
        var successResponse = HandleSuspendAfterProtectionReleased(
            pendingSuspendContext,
            stoppedSnapshot,
            eventName,
            successMessage,
            _sessionRegistry.ActiveSessionCount);
        AppendSessionLog(eventName, request, successResponse, stoppedSnapshot, commandName);
        return successResponse;
    }

    private LidGuardPipeResponse HandleSuspendAfterProtectionReleased(
        PendingSuspendContext pendingSuspendContext,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        string successMessage,
        int activeSessionCount)
    {
        var lidSwitchState = _lidStateSource.CurrentState;
        if (lidSwitchState == LidSwitchState.Open)
        {
            var response = CreateSuccessResponse("Skipped suspend because the lid is open.");
            AppendSessionLog($"{eventName}-suspend-skipped", pendingSuspendContext, response, snapshot);
            return CreateSuccessResponse(successMessage);
        }

        if (lidSwitchState != LidSwitchState.Closed)
        {
            var response = CreateSuccessResponse("Skipped suspend because the lid state is unknown.");
            AppendSessionLog($"{eventName}-suspend-skipped", pendingSuspendContext, response, snapshot);
            return CreateSuccessResponse(successMessage);
        }

        var suspendMode = _settings.SuspendMode;
        var postStopSuspendDelaySeconds = _settings.PostStopSuspendDelaySeconds;
        var scheduledResponse = CreateSuccessResponse(
            $"Scheduled {suspendMode} {DescribePostStopSuspendDelay(postStopSuspendDelaySeconds)} {DescribeSuspendReason(activeSessionCount)}");
        AppendSessionLog($"{eventName}-suspend-scheduled", pendingSuspendContext, scheduledResponse, snapshot);
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

            if (postStopSuspendDelaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(postStopSuspendDelaySeconds), pendingSuspendCancellationTokenSource.Token);

            var postStopSuspendSound = string.Empty;
            await _gate.WaitAsync(pendingSuspendCancellationTokenSource.Token);
            try
            {
                if (HasSessionsKeepingProtectionAppliedInsideGate())
                {
                    var canceledResponse = CreateSuccessResponse("Skipped pending suspend because a session became active before suspend ran.");
                    AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                    return;
                }

                var lidSwitchState = _lidStateSource.CurrentState;
                if (lidSwitchState != LidSwitchState.Closed)
                {
                    var canceledResponse = CreateSuccessResponse($"Skipped suspend because the lid state is {lidSwitchState} before suspend ran.");
                    AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                    return;
                }

                postStopSuspendSound = _settings.PostStopSuspendSound;
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
                pendingSuspendCancellationTokenSource.Token);
            await RequestSuspendAsync(
                pendingSuspendContext,
                snapshot,
                eventName,
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
        CancellationToken cancellationToken)
    {
        var suspendMode = SystemSuspendMode.Sleep;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (HasSessionsKeepingProtectionAppliedInsideGate())
            {
                var canceledResponse = CreateSuccessResponse("Skipped pending suspend because a session became active before suspend was requested.");
                AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                return;
            }

            var lidSwitchState = _lidStateSource.CurrentState;
            if (lidSwitchState != LidSwitchState.Closed)
            {
                var canceledResponse = CreateSuccessResponse($"Skipped suspend because the lid state is {lidSwitchState} before suspend was requested.");
                AppendSessionLog($"{eventName}-suspend-canceled", pendingSuspendContext, canceledResponse, snapshot);
                return;
            }

            suspendMode = _settings.SuspendMode;
            var requestingResponse = CreateSuccessResponse($"Requesting {suspendMode} {DescribeSuspendReason(_sessionRegistry.ActiveSessionCount)}");
            AppendSessionLog($"{eventName}-suspend-requesting", pendingSuspendContext, requestingResponse, snapshot);
        }
        finally
        {
            _gate.Release();
        }

        var suspendResult = _systemSuspendService.Suspend(suspendMode);
        if (suspendResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(suspendResult);
            AppendSessionLog($"{eventName}-suspend-failed", pendingSuspendContext, response, snapshot);
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
            if (ReferenceEquals(_pendingSuspendCancellationTokenSource, pendingSuspendCancellationTokenSource))
                _pendingSuspendCancellationTokenSource = null;
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(postStopSuspendSound)) return;

        var playbackResult = await _postStopSuspendSoundPlayer.PlayAsync(postStopSuspendSound, cancellationToken);
        if (playbackResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(playbackResult);
            AppendSessionLog($"{eventName}-suspend-sound-failed", pendingSuspendContext, response, snapshot);
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
            cancellationToken);
        if (sendResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var response = CreateFailureResponse(sendResult);
            AppendSessionLog($"{eventName}-suspend-webhook-failed", pendingSuspendContext, response, snapshot);
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

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (!TryValidateEmergencyHibernationStateInsideGate(
                emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
                out emergencyHibernationTemperatureCelsius,
                out var canceledMessage))
            {
                AppendEmergencyHibernationLog(
                    "emergency-hibernation-canceled",
                    CreateSuccessResponse(canceledMessage),
                    emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
                    emergencyHibernationTemperatureCelsius);
                return;
            }

            CancelPendingSuspend();
            AppendEmergencyHibernationLog(
                "emergency-hibernation-thermal-detected",
                CreateSuccessResponse(
                    $"Detected high system temperature {DescribeEmergencyHibernationTemperature(emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius, emergencyHibernationTemperatureCelsius)}. Requesting Emergency Hibernation."),
                emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius);
        }
        finally
        {
            _gate.Release();
        }

        await SendEmergencyHibernationWebhookAsync(
            emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
            emergencyHibernationTemperatureCelsius);
        await RequestEmergencyHibernationAsync(
            emergencyHibernationThermalThresholdReachedContext.ObservedTemperatureCelsius,
            emergencyHibernationTemperatureCelsius);
    }

    private async Task SendEmergencyHibernationWebhookAsync(
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius)
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
            AppendEmergencyHibernationLog(
                "emergency-hibernation-webhook-failed",
                CreateFailureResponse(sendResult),
                observedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RequestEmergencyHibernationAsync(
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (!TryValidateEmergencyHibernationStateInsideGate(
                observedTemperatureCelsius,
                out emergencyHibernationTemperatureCelsius,
                out var canceledMessage))
            {
                AppendEmergencyHibernationLog(
                    "emergency-hibernation-canceled",
                    CreateSuccessResponse(canceledMessage),
                    observedTemperatureCelsius,
                    emergencyHibernationTemperatureCelsius);
                return;
            }

            AppendEmergencyHibernationLog(
                "emergency-hibernation-requesting",
                CreateSuccessResponse(
                    $"Requesting Emergency Hibernation because system temperature reached {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius)}."),
                observedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius);
        }
        finally
        {
            _gate.Release();
        }

        var hibernationResult = _systemSuspendService.Suspend(SystemSuspendMode.Hibernate);
        if (hibernationResult.Succeeded) return;

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            AppendEmergencyHibernationLog(
                "emergency-hibernation-failed",
                CreateFailureResponse(hibernationResult),
                observedTemperatureCelsius,
                emergencyHibernationTemperatureCelsius);
        }
        finally
        {
            _gate.Release();
        }
    }

    private EmergencyHibernationThermalMonitorState CreateEmergencyHibernationThermalMonitorState()
        => new(
            _protectionApplied,
            _settings.EmergencyHibernationOnHighTemperature,
            _lidStateSource.CurrentState,
            LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius(_settings.EmergencyHibernationTemperatureCelsius));

    private bool TryValidateEmergencyHibernationStateInsideGate(
        int observedTemperatureCelsius,
        out int emergencyHibernationTemperatureCelsius,
        out string message)
    {
        emergencyHibernationTemperatureCelsius = LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius(_settings.EmergencyHibernationTemperatureCelsius);
        message = string.Empty;

        if (!_protectionApplied)
        {
            message = "Skipped Emergency Hibernation because guard protection is no longer applied.";
            return false;
        }

        if (!_settings.EmergencyHibernationOnHighTemperature)
        {
            message = "Skipped Emergency Hibernation because high-temperature Emergency Hibernation is disabled.";
            return false;
        }

        var lidSwitchState = _lidStateSource.CurrentState;
        if (lidSwitchState != LidSwitchState.Closed)
        {
            message = $"Skipped Emergency Hibernation because the lid state is {lidSwitchState}.";
            return false;
        }

        if (observedTemperatureCelsius < emergencyHibernationTemperatureCelsius)
        {
            message =
                $"Skipped Emergency Hibernation because the observed temperature {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius)} is no longer above the current threshold.";
            return false;
        }

        return true;
    }

    private LidGuardPipeResponse CreateSuccessResponse(string message)
    {
        var snapshots = _sessionRegistry.GetSnapshots();
        return LidGuardPipeResponse.Success(message, snapshots.Count, CreateSessionStatuses(snapshots), _settings, _lidStateSource.CurrentState);
    }

    private LidGuardPipeResponse CreateFailureResponse(LidGuardOperationResult result)
    {
        var snapshots = _sessionRegistry.GetSnapshots();
        return LidGuardPipeResponse.Failure(CreateResultMessage(result), snapshots.Count);
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

    private static string DescribePostStopSuspendDelay(int postStopSuspendDelaySeconds)
        => postStopSuspendDelaySeconds == 0 ? "immediately" : $"in {postStopSuspendDelaySeconds} second(s)";

    private static SuspendWebhookReason CreateSuspendWebhookReason(int activeSessionCount)
        => activeSessionCount == 0 ? SuspendWebhookReason.Completed : SuspendWebhookReason.SoftLocked;

    private static string DescribeSuspendReason(int activeSessionCount)
        => activeSessionCount == 0
            ? "because the lid is closed after the last session stopped."
            : "because the lid is closed and all remaining sessions are soft-locked.";

    private static string DescribeEmergencyHibernationTemperature(
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius)
        => $"{observedTemperatureCelsius} Celsius (threshold: {emergencyHibernationTemperatureCelsius} Celsius)";

    private static bool TryExtractPostStopScheduleMessage(string responseMessage, out string postStopScheduleMessage)
    {
        postStopScheduleMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(responseMessage)) return false;

        var scheduledIndex = responseMessage.IndexOf("Scheduled ", StringComparison.Ordinal);
        if (scheduledIndex < 0) return false;

        postStopScheduleMessage = responseMessage[scheduledIndex..];
        return true;
    }

    private static void AppendRuntimeLog(string eventName, string command, LidGuardPipeResponse response)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = command,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private void AppendEmergencyHibernationLog(
        string eventName,
        LidGuardPipeResponse response,
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = EmergencyHibernationMonitorCommandName,
            Succeeded = response.Succeeded,
            Message = $"{response.Message} Observed temperature: {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius)}.",
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static void AppendSessionLog(string eventName, LidGuardPipeRequest request, LidGuardPipeResponse response, int watchedProcessIdentifier = 0)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = request.Command,
            Provider = request.Provider,
            ProviderName = AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName),
            SessionIdentifier = request.SessionIdentifier,
            SoftLockState = request.Command == LidGuardPipeCommands.MarkSessionSoftLocked ? LidGuardSessionSoftLockState.SoftLocked : LidGuardSessionSoftLockState.None,
            SoftLockReason = request.SessionStateReason,
            WatchedProcessIdentifier = watchedProcessIdentifier > 0 ? watchedProcessIdentifier : request.WatchedProcessIdentifier,
            WorkingDirectory = request.WorkingDirectory,
            TranscriptPath = request.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static void AppendSessionLog(string eventName, LidGuardPipeRequest request, LidGuardPipeResponse response, LidGuardSessionSnapshot snapshot)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = request.Command,
            Provider = request.Provider,
            ProviderName = snapshot.ProviderName,
            SessionIdentifier = request.SessionIdentifier,
            SoftLockState = snapshot.SoftLockState,
            SoftLockReason = snapshot.SoftLockReason,
            SoftLockedAt = snapshot.SoftLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = snapshot.WorkingDirectory,
            TranscriptPath = snapshot.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static void AppendSessionLog(string eventName, LidGuardSessionStopRequest request, LidGuardPipeResponse response, string commandName)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = commandName,
            Provider = request.Provider,
            ProviderName = AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName),
            SessionIdentifier = request.SessionIdentifier,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static void AppendSessionLog(
        string eventName,
        LidGuardSessionStopRequest request,
        LidGuardPipeResponse response,
        LidGuardSessionSnapshot snapshot,
        string commandName)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = commandName,
            Provider = request.Provider,
            ProviderName = snapshot.ProviderName,
            SessionIdentifier = request.SessionIdentifier,
            SoftLockState = snapshot.SoftLockState,
            SoftLockReason = snapshot.SoftLockReason,
            SoftLockedAt = snapshot.SoftLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = snapshot.WorkingDirectory,
            TranscriptPath = snapshot.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static void AppendSessionLog(
        string eventName,
        PendingSuspendContext pendingSuspendContext,
        LidGuardPipeResponse response,
        LidGuardSessionSnapshot snapshot)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = pendingSuspendContext.CommandName,
            Provider = pendingSuspendContext.Provider,
            ProviderName = pendingSuspendContext.ProviderName,
            SessionIdentifier = pendingSuspendContext.SessionIdentifier,
            SoftLockState = snapshot.SoftLockState,
            SoftLockReason = snapshot.SoftLockReason,
            SoftLockedAt = snapshot.SoftLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = pendingSuspendContext.WorkingDirectory,
            TranscriptPath = snapshot.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private LidGuardPipeResponse MarkSessionActiveInsideGate(LidGuardPipeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var rejectedResponse = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            AppendSessionLog("session-activity-rejected", request, rejectedResponse);
            return rejectedResponse;
        }

        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);
        if (!_sessionRegistry.TryMarkActive(request.Provider, request.SessionIdentifier, request.ProviderName, out var snapshot, out var changed))
        {
            var ignoredResponse = CreateSuccessResponse($"Session {key} is not active; ignored activity signal.");
            AppendSessionLog("session-activity-ignored", request, ignoredResponse);
            return ignoredResponse;
        }

        _codexSoftLockTranscriptMonitor.ResetSession(key);
        CancelPendingSuspend();
        var protectionResult = EnsureProtection();
        if (!protectionResult.Succeeded)
        {
            var failedResponse = CreateFailureResponse(protectionResult);
            AppendSessionLog("session-activity-failed", request, failedResponse, snapshot);
            return failedResponse;
        }

        var successMessage = changed
            ? $"Cleared soft-lock for {key} because activity was detected from {request.SessionStateReason}."
            : $"Session {key} was already active.";
        var successResponse = CreateSuccessResponse(successMessage);
        AppendSessionLog("session-activity-recorded", request, successResponse, snapshot);
        return successResponse;
    }

    private void AppendCodexTranscriptMonitorRegistration(
        LidGuardPipeRequest request,
        LidGuardSessionSnapshot snapshot,
        CodexTranscriptMonitoringRegistrationResult codexTranscriptMonitoringRegistrationResult)
    {
        if (request.Provider != AgentProvider.Codex) return;
        if (string.IsNullOrWhiteSpace(codexTranscriptMonitoringRegistrationResult.Message)) return;

        var response = CreateSuccessResponse(codexTranscriptMonitoringRegistrationResult.Message);
        var eventName = codexTranscriptMonitoringRegistrationResult.MonitoringEnabled
            ? "codex-transcript-monitor-configured"
            : "codex-transcript-monitor-skipped";
        AppendSessionLog(eventName, request, response, snapshot);
    }

    private async Task HandleCodexTranscriptActivityThresholdReachedAsync(CodexTranscriptActivityThresholdReachedContext transcriptActivityThresholdReachedContext)
    {
        var request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.MarkSessionActive,
            Provider = AgentProvider.Codex,
            SessionIdentifier = transcriptActivityThresholdReachedContext.SessionKey.SessionIdentifier,
            SessionStateReason = $"codex_transcript_activity_threshold_reached:{transcriptActivityThresholdReachedContext.ActualContentIncreaseCount}",
            WorkingDirectory = transcriptActivityThresholdReachedContext.WorkingDirectory,
            TranscriptPath = transcriptActivityThresholdReachedContext.TranscriptPath
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

    private readonly record struct PendingSuspendContext(
        AgentProvider Provider,
        string ProviderName,
        string SessionIdentifier,
        string WorkingDirectory,
        string CommandName,
        string SessionStateReason);
}


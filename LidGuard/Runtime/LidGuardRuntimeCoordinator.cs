using System.Diagnostics;
using LidGuard.Ipc;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Runtime;

internal sealed class LidGuardRuntimeCoordinator(
    LidGuardSettings initialSettings,
    IPowerRequestService powerRequestService,
    ICommandLineProcessResolver commandLineProcessResolver,
    IProcessExitWatcher processExitWatcher,
    LidActionPolicyController lidActionPolicyController,
    ISystemSuspendService systemSuspendService,
    ILidStateSource lidStateSource)
{
    private static readonly TimeSpan s_processWatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_suspendRequestDelay = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LidGuardSessionRegistry _sessionRegistry = new();
    private readonly Dictionary<LidGuardSessionKey, CancellationTokenSource> _watcherCancellationTokenSources = [];
    private readonly LidGuardPendingLidActionBackupManager _pendingLidActionBackupManager = new(lidActionPolicyController);

    private LidGuardSettings _settings = LidGuardSettings.Normalize(initialSettings);
    private ILidGuardPowerRequest _powerRequest = InactiveLidGuardPowerRequest.Instance;
    private LidActionBackup _lidActionBackup;
    private bool _hasLidActionBackup;
    private bool _protectionApplied;

    public async Task<LidGuardPipeResponse> HandleAsync(LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Command switch
        {
            LidGuardPipeCommands.Start => await StartAsync(request, cancellationToken),
            LidGuardPipeCommands.Stop => await StopAsync(request, cancellationToken),
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

            var watchedProcessIdentifier = ResolveWatchedProcessIdentifier(request);
            var startRequest = new LidGuardSessionStartRequest
            {
                SessionIdentifier = request.SessionIdentifier,
                Provider = request.Provider,
                StartedAt = DateTimeOffset.UtcNow,
                WatchedProcessIdentifier = watchedProcessIdentifier,
                WorkingDirectory = request.WorkingDirectory
            };

            var snapshot = _sessionRegistry.StartOrUpdate(startRequest);
            var protectionResult = EnsureProtection();
            if (!protectionResult.Succeeded)
            {
                _sessionRegistry.Stop(new LidGuardSessionStopRequest { SessionIdentifier = request.SessionIdentifier, Provider = request.Provider }, out _);
                var response = CreateFailureResponse(protectionResult);
                AppendSessionLog("session-start-failed", request, response);
                return response;
            }

            StartWatcher(snapshot);

            var watchMessage = snapshot.HasWatchedProcess ? $" Watching process {snapshot.WatchedProcessIdentifier}." : " No watched process was resolved; a stop hook is required.";
            var successResponse = CreateSuccessResponse($"Started {snapshot.Key}.{watchMessage}");
            AppendSessionLog("session-started", request, successResponse, snapshot.WatchedProcessIdentifier);
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
            var stopRequest = new LidGuardSessionStopRequest { SessionIdentifier = request.SessionIdentifier, Provider = request.Provider };
            return StopInsideGate(stopRequest, $"Stopped {stopRequest.Provider}:{stopRequest.SessionIdentifier}.");
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
                    new LidGuardSessionStopRequest { SessionIdentifier = snapshot.SessionIdentifier, Provider = snapshot.Provider },
                    $"Cleaned orphan session {snapshot.Key}.",
                    "orphan-session-cleaned");

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
        if (!_settings.WatchParentProcess) return 0;
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory)) return 0;

        var resolveResult = commandLineProcessResolver.FindForWorkingDirectory(request.WorkingDirectory, request.Provider);
        return resolveResult.Succeeded ? resolveResult.Value.ProcessIdentifier : 0;
    }

    private LidGuardOperationResult UpdateSettingsInsideGate(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (!_sessionRegistry.HasActiveSessions)
        {
            _settings = normalizedSettings;
            ReconfigureWatchers();
            return LidGuardOperationResult.Success();
        }

        var previousSettings = _settings;
        var restoreResult = RestoreProtection();
        if (!restoreResult.Succeeded) return restoreResult;

        _settings = normalizedSettings;
        var protectionResult = EnsureProtection();
        if (protectionResult.Succeeded)
        {
            ReconfigureWatchers();
            return LidGuardOperationResult.Success();
        }

        _settings = previousSettings;
        var rollbackResult = EnsureProtection();
        if (!rollbackResult.Succeeded)
        {
            var message = $"{CreateResultMessage(protectionResult)} Rollback failed: {CreateResultMessage(rollbackResult)}";
            return LidGuardOperationResult.Failure(message);
        }

        ReconfigureWatchers();
        return protectionResult;
    }

    private LidGuardOperationResult EnsureProtection()
    {
        if (_protectionApplied) return LidGuardOperationResult.Success();

        var powerRequestResult = powerRequestService.Create(_settings.PowerRequest);
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

    private async Task WatchProcessExitAsync(LidGuardSessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var watchResult = await processExitWatcher.WaitForExitAsync(snapshot.WatchedProcessIdentifier, s_processWatchInterval, cancellationToken);
            if (!watchResult.Succeeded) return;

            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                StopInsideGate(
                    new LidGuardSessionStopRequest { SessionIdentifier = snapshot.SessionIdentifier, Provider = snapshot.Provider },
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

    private LidGuardPipeResponse StopInsideGate(LidGuardSessionStopRequest request, string successMessage, string eventName = "session-stopped")
    {
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier))
        {
            var response = LidGuardPipeResponse.Failure("A session identifier is required.", _sessionRegistry.ActiveSessionCount);
            AppendSessionLog("session-stop-rejected", request, response);
            return response;
        }

        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier);
        CancelWatcher(key);

        if (!_sessionRegistry.Stop(request, out var stoppedSnapshot))
        {
            var response = CreateSuccessResponse($"Session {key} is already stopped.");
            AppendSessionLog($"{eventName}-already-stopped", request, response);
            return response;
        }

        if (_sessionRegistry.HasActiveSessions)
        {
            var response = CreateSuccessResponse(successMessage);
            AppendSessionLog(eventName, request, response, stoppedSnapshot);
            return response;
        }

        var restoreResult = RestoreProtection();
        var restoreResponse = restoreResult.Succeeded ? CreateSuccessResponse(successMessage) : CreateFailureResponse(restoreResult);
        AppendSessionLog(eventName, request, restoreResponse, stoppedSnapshot);
        if (!restoreResult.Succeeded) return restoreResponse;

        return HandlePostStopSuspend(request, stoppedSnapshot, eventName, successMessage, restoreResponse);
    }

    private LidGuardPipeResponse HandlePostStopSuspend(
        LidGuardSessionStopRequest request,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        string successMessage,
        LidGuardPipeResponse restoreResponse)
    {
        var lidSwitchState = lidStateSource.CurrentState;
        if (lidSwitchState == LidSwitchState.Open)
        {
            var response = CreateSuccessResponse("Skipped post-stop suspend because the lid is open.");
            AppendSessionLog($"{eventName}-suspend-skipped", request, response, snapshot);
            return restoreResponse;
        }

        if (lidSwitchState != LidSwitchState.Closed)
        {
            var response = CreateSuccessResponse("Skipped post-stop suspend because the lid state is unknown.");
            AppendSessionLog($"{eventName}-suspend-skipped", request, response, snapshot);
            return restoreResponse;
        }

        var suspendMode = _settings.SuspendMode;
        var scheduledResponse = CreateSuccessResponse($"Scheduled {suspendMode} because the lid is closed after the last session stopped.");
        AppendSessionLog($"{eventName}-suspend-scheduled", request, scheduledResponse, snapshot);
        _ = SuspendAfterDelayAsync(request, snapshot, eventName, suspendMode);
        return CreateSuccessResponse($"{successMessage} Scheduled {suspendMode} because the lid is closed.");
    }

    private async Task SuspendAfterDelayAsync(
        LidGuardSessionStopRequest request,
        LidGuardSessionSnapshot snapshot,
        string eventName,
        SystemSuspendMode suspendMode)
    {
        try
        {
            await Task.Delay(s_suspendRequestDelay);

            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (_sessionRegistry.HasActiveSessions)
                {
                    var canceledResponse = CreateSuccessResponse("Skipped post-stop suspend because a new session started before suspend ran.");
                    AppendSessionLog($"{eventName}-suspend-canceled", request, canceledResponse, snapshot);
                    return;
                }

                var lidSwitchState = lidStateSource.CurrentState;
                if (lidSwitchState != LidSwitchState.Closed)
                {
                    var canceledResponse = CreateSuccessResponse($"Skipped post-stop suspend because the lid state is {lidSwitchState} before suspend ran.");
                    AppendSessionLog($"{eventName}-suspend-canceled", request, canceledResponse, snapshot);
                    return;
                }

                var requestingResponse = CreateSuccessResponse($"Requesting {suspendMode} because the lid is closed after the last session stopped.");
                AppendSessionLog($"{eventName}-suspend-requesting", request, requestingResponse, snapshot);
            }
            finally
            {
                _gate.Release();
            }

            var suspendResult = systemSuspendService.Suspend(suspendMode);
            if (suspendResult.Succeeded) return;

            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                var response = CreateFailureResponse(suspendResult);
                AppendSessionLog($"{eventName}-suspend-failed", request, response, snapshot);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) { }
    }

    private void CancelWatcher(LidGuardSessionKey key)
    {
        if (!_watcherCancellationTokenSources.Remove(key, out var cancellationTokenSource)) return;

        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }

    private LidGuardPipeResponse CreateSuccessResponse(string message)
    {
        var snapshots = _sessionRegistry.GetSnapshots();
        return LidGuardPipeResponse.Success(message, snapshots.Count, CreateSessionStatuses(snapshots), _settings, lidStateSource.CurrentState);
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
                StartedAt = snapshot.StartedAt,
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

    private static void AppendSessionLog(string eventName, LidGuardPipeRequest request, LidGuardPipeResponse response, int watchedProcessIdentifier = 0)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = request.Command,
            Provider = request.Provider,
            SessionIdentifier = request.SessionIdentifier,
            WatchedProcessIdentifier = watchedProcessIdentifier > 0 ? watchedProcessIdentifier : request.WatchedProcessIdentifier,
            WorkingDirectory = request.WorkingDirectory,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static void AppendSessionLog(string eventName, LidGuardSessionStopRequest request, LidGuardPipeResponse response)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = LidGuardPipeCommands.Stop,
            Provider = request.Provider,
            SessionIdentifier = request.SessionIdentifier,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static void AppendSessionLog(string eventName, LidGuardSessionStopRequest request, LidGuardPipeResponse response, LidGuardSessionSnapshot snapshot)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = LidGuardPipeCommands.Stop,
            Provider = request.Provider,
            SessionIdentifier = request.SessionIdentifier,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = snapshot.WorkingDirectory,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
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
}


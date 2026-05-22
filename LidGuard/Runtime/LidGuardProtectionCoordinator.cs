using LidGuard.Ipc;
using LidGuard.Power;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal sealed class LidGuardProtectionCoordinator(IPowerRequestService powerRequestService, LidActionPolicyController lidActionPolicyController)
{
    private readonly LidGuardPendingLidActionBackupManager _pendingLidActionBackupManager = new(lidActionPolicyController);
    private ILidGuardPowerRequest _powerRequest = InactiveLidGuardPowerRequest.Instance;
    private bool _hasTemporaryLidActionPolicy;

    public bool IsApplied { get; private set; }

    public LidGuardOperationResult Ensure(LidGuardSettings settings)
    {
        if (IsApplied) return LidGuardOperationResult.Success();

        var powerRequestResult = powerRequestService.Create(settings.PowerRequest);
        if (!powerRequestResult.Succeeded) return LidGuardOperationResult.Failure(powerRequestResult.Message, powerRequestResult.NativeErrorCode);

        _powerRequest = powerRequestResult.Value;

        if (settings.ChangeLidAction)
        {
            var lidActionResult = _pendingLidActionBackupManager.ApplyTemporaryDoNothing(settings);
            if (!lidActionResult.Succeeded)
            {
                Restore();
                return LidGuardOperationResult.Failure(lidActionResult.Message, lidActionResult.NativeErrorCode);
            }

            _hasTemporaryLidActionPolicy = true;
        }

        IsApplied = true;
        return LidGuardOperationResult.Success();
    }

    public LidGuardOperationResult Restore()
    {
        var restoreMessages = new List<string>();

        if (_hasTemporaryLidActionPolicy)
        {
            var restoreResult = _pendingLidActionBackupManager.RestorePendingBackupIfPresent();
            if (!restoreResult.Succeeded) restoreMessages.Add(CreateResultMessage(restoreResult));
            else if (!restoreResult.Value) AppendMissingPendingBackupRestoreLog();
            _hasTemporaryLidActionPolicy = false;
        }

        DisposePowerRequest();
        IsApplied = false;

        return restoreMessages.Count == 0 ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(string.Join(" ", restoreMessages));
    }

    private void DisposePowerRequest()
    {
        _powerRequest.Dispose();
        _powerRequest = InactiveLidGuardPowerRequest.Instance;
    }

    private static string CreateResultMessage(LidGuardOperationResult result)
    {
        if (result.NativeErrorCode == 0) return result.Message;
        return $"{result.Message} Native error: {result.NativeErrorCode}.";
    }

    private static string CreateResultMessage(LidGuardOperationResult<bool> result)
    {
        if (result.NativeErrorCode == 0) return result.Message;
        return $"{result.Message} Native error: {result.NativeErrorCode}.";
    }

    private static void AppendMissingPendingBackupRestoreLog()
    {
        var backupFilePath = LidGuardPendingLidActionBackupStore.GetDefaultFilePath();
        var message = $"Skipped lid close policy restore because the pending backup JSON was missing at {backupFilePath}.";
        LidGuardRuntimeLogWriter.AppendRuntimeLog("lid-action-restore-missing-backup", "lid-action-restore", LidGuardPipeResponse.Failure(message));
    }
}

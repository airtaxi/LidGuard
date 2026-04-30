using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Runtime;

internal sealed class LidGuardProtectionCoordinator(
    IPowerRequestService powerRequestService,
    LidActionPolicyController lidActionPolicyController)
{
    private readonly LidGuardPendingLidActionBackupManager _pendingLidActionBackupManager = new(lidActionPolicyController);
    private ILidGuardPowerRequest _powerRequest = InactiveLidGuardPowerRequest.Instance;
    private LidActionBackup _lidActionBackup;
    private bool _hasLidActionBackup;

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

            _lidActionBackup = lidActionResult.Value;
            _hasLidActionBackup = true;
        }

        IsApplied = true;
        return LidGuardOperationResult.Success();
    }

    public LidGuardOperationResult Restore()
    {
        var restoreMessages = new List<string>();

        if (_hasLidActionBackup)
        {
            var restoreResult = _pendingLidActionBackupManager.Restore(_lidActionBackup);
            if (!restoreResult.Succeeded) restoreMessages.Add(CreateResultMessage(restoreResult));
            _hasLidActionBackup = false;
        }

        DisposePowerRequest();
        IsApplied = false;

        return restoreMessages.Count == 0
            ? LidGuardOperationResult.Success()
            : LidGuardOperationResult.Failure(string.Join(" ", restoreMessages));
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
}

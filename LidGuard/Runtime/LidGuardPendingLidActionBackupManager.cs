using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Runtime;

internal sealed class LidGuardPendingLidActionBackupManager(LidActionPolicyController lidActionPolicyController)
{
    public LidGuardOperationResult<LidActionBackup> ApplyTemporaryDoNothing(LidGuardSettings settings)
    {
        var captureResult = lidActionPolicyController.CaptureBackup(settings);
        if (!captureResult.Succeeded) return captureResult;

        var backup = captureResult.Value;
        if (!LidGuardPendingLidActionBackupStore.TrySave(backup, out var saveMessage)) return LidGuardOperationResult<LidActionBackup>.Failure(saveMessage);

        var applyResult = lidActionPolicyController.ApplyTemporaryDoNothing(backup);
        if (applyResult.Succeeded) return LidGuardOperationResult<LidActionBackup>.Success(backup);

        var rollbackResult = RollBackFailedApply(backup, applyResult);
        if (!rollbackResult.Succeeded) return LidGuardOperationResult<LidActionBackup>.Failure(rollbackResult.Message, rollbackResult.NativeErrorCode);

        return LidGuardOperationResult<LidActionBackup>.Failure(CreateResultMessage(applyResult), applyResult.NativeErrorCode);
    }

    public LidGuardOperationResult Restore(LidActionBackup backup)
    {
        var restoreResult = lidActionPolicyController.Restore(backup);
        if (!restoreResult.Succeeded) return restoreResult;

        if (LidGuardPendingLidActionBackupStore.TryDelete(out var deleteMessage)) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(deleteMessage);
    }

    public LidGuardOperationResult<bool> RestorePendingBackupIfPresent()
    {
        if (!LidGuardPendingLidActionBackupStore.TryLoad(out var backup, out var hasBackup, out var loadMessage))
        {
            return LidGuardOperationResult<bool>.Failure(loadMessage);
        }

        if (!hasBackup) return LidGuardOperationResult<bool>.Success(false);

        var restoreResult = Restore(backup);
        if (!restoreResult.Succeeded) return LidGuardOperationResult<bool>.Failure(CreateResultMessage(restoreResult), restoreResult.NativeErrorCode);

        return LidGuardOperationResult<bool>.Success(true);
    }

    private LidGuardOperationResult RollBackFailedApply(LidActionBackup backup, LidGuardOperationResult applyResult)
    {
        var rollbackRestoreResult = lidActionPolicyController.Restore(backup);
        if (!rollbackRestoreResult.Succeeded)
        {
            var message = $"{CreateResultMessage(applyResult)} Rollback failed: {CreateResultMessage(rollbackRestoreResult)}";
            return LidGuardOperationResult.Failure(message, GetNativeErrorCode(applyResult, rollbackRestoreResult));
        }

        if (LidGuardPendingLidActionBackupStore.TryDelete(out var deleteMessage)) return LidGuardOperationResult.Success();

        var cleanupFailureMessage = $"{CreateResultMessage(applyResult)} Pending backup cleanup failed after rollback: {deleteMessage}";
        return LidGuardOperationResult.Failure(cleanupFailureMessage, GetNativeErrorCode(applyResult));
    }

    private static int GetNativeErrorCode(params LidGuardOperationResult[] results)
    {
        foreach (var result in results)
        {
            if (result.NativeErrorCode != 0) return result.NativeErrorCode;
        }

        return 0;
    }

    private static string CreateResultMessage(LidGuardOperationResult result)
    {
        if (result.NativeErrorCode == 0) return result.Message;
        return $"{result.Message} Native error: {result.NativeErrorCode}.";
    }
}

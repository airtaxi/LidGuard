using LidGuard.Power;
using LidGuard.Results;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal sealed class LidGuardPendingLidActionBackupManager(LidActionPolicyController lidActionPolicyController)
{
    public LidGuardOperationResult ApplyTemporaryDoNothing(LidGuardSettings settings)
    {
        if (!LidGuardPendingLidActionBackupStore.TryLoad(out var existingBackup, out var hasExistingBackup, out var loadMessage)) return LidGuardOperationResult.Failure(loadMessage);
        if (hasExistingBackup) return ApplyExistingBackup(existingBackup);

        var captureResult = lidActionPolicyController.CaptureBackup(settings);
        if (!captureResult.Succeeded) return LidGuardOperationResult.Failure(captureResult.Message, captureResult.NativeErrorCode);

        var backup = captureResult.Value;
        if (!LidGuardPendingLidActionBackupStore.TrySaveIfMissing(backup, out var saved, out var saveMessage)) return LidGuardOperationResult.Failure(saveMessage);
        if (!saved) return ApplyExistingBackupFromStore();

        var applyResult = lidActionPolicyController.ApplyTemporaryDoNothing(backup);
        if (applyResult.Succeeded) return LidGuardOperationResult.Success();

        var rollbackResult = RollBackFailedApply(backup, applyResult);
        if (!rollbackResult.Succeeded) return LidGuardOperationResult.Failure(rollbackResult.Message, rollbackResult.NativeErrorCode);

        return LidGuardOperationResult.Failure(CreateResultMessage(applyResult), applyResult.NativeErrorCode);
    }

    public LidGuardOperationResult<bool> RestorePendingBackupIfPresent()
    {
        if (!LidGuardPendingLidActionBackupStore.TryLoad(out var backup, out var hasBackup, out var loadMessage)) return LidGuardOperationResult<bool>.Failure(loadMessage);

        if (!hasBackup) return LidGuardOperationResult<bool>.Success(false);

        var restoreResult = Restore(backup);
        if (!restoreResult.Succeeded) return LidGuardOperationResult<bool>.Failure(CreateResultMessage(restoreResult), restoreResult.NativeErrorCode);

        return LidGuardOperationResult<bool>.Success(true);
    }

    private LidGuardOperationResult ApplyExistingBackupFromStore()
    {
        if (!LidGuardPendingLidActionBackupStore.TryLoad(out var backup, out var hasBackup, out var loadMessage)) return LidGuardOperationResult.Failure(loadMessage);
        if (!hasBackup) return LidGuardOperationResult.Failure($"Skipped writing a new pending lid action backup, but no existing backup was found at {LidGuardPendingLidActionBackupStore.GetDefaultFilePath()}.");

        return ApplyExistingBackup(backup);
    }

    private LidGuardOperationResult ApplyExistingBackup(LidActionBackup backup)
    {
        var applyResult = lidActionPolicyController.ApplyTemporaryDoNothing(backup);
        if (applyResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(CreateResultMessage(applyResult), applyResult.NativeErrorCode);
    }

    private LidGuardOperationResult Restore(LidActionBackup backup)
    {
        var restoreResult = lidActionPolicyController.Restore(backup);
        if (!restoreResult.Succeeded) return restoreResult;

        if (LidGuardPendingLidActionBackupStore.TryDelete(out var deleteMessage)) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(deleteMessage);
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
        foreach (var result in results) if (result.NativeErrorCode != 0) return result.NativeErrorCode;

        return 0;
    }

    private static string CreateResultMessage(LidGuardOperationResult result)
    {
        if (result.NativeErrorCode == 0) return result.Message;
        return $"{result.Message} Native error: {result.NativeErrorCode}.";
    }
}

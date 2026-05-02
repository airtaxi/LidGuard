using LidGuard.Power;
using LidGuard.Results;

namespace LidGuard.Runtime;

internal static class MacOSPendingPowerStateBackupManager
{
    public static LidGuardOperationResult<bool> RestorePendingBackupIfPresent()
    {
        if (!MacOSPendingPowerStateBackupStore.TryLoad(out var state, out var hasBackup, out var loadMessage))
            return LidGuardOperationResult<bool>.Failure(loadMessage);
        if (!hasBackup) return LidGuardOperationResult<bool>.Success(false);

        var restoreResult = Restore(state);
        if (!restoreResult.Succeeded) return LidGuardOperationResult<bool>.Failure(restoreResult.Message, restoreResult.NativeErrorCode);

        return LidGuardOperationResult<bool>.Success(true);
    }

    public static LidGuardOperationResult<MacOSPendingPowerStateBackupState> CaptureHibernateMode()
    {
        var hibernateModeResult = MacOSPowerSettings.ReadHibernateMode();
        if (!hibernateModeResult.Succeeded) return LidGuardOperationResult<MacOSPendingPowerStateBackupState>.Failure(hibernateModeResult.Message, hibernateModeResult.NativeErrorCode);
        if (!MacOSPowerSettings.IsSupportedHibernateMode(hibernateModeResult.Value))
            return LidGuardOperationResult<MacOSPendingPowerStateBackupState>.Failure($"The current macOS hibernatemode value is unsupported by LidGuard: {hibernateModeResult.Value}.");

        var state = new MacOSPendingPowerStateBackupState
        {
            IncludesHibernateMode = true,
            HibernateMode = hibernateModeResult.Value
        };
        if (!MacOSPendingPowerStateBackupStore.TrySave(state, out var saveMessage))
            return LidGuardOperationResult<MacOSPendingPowerStateBackupState>.Failure(saveMessage);

        return LidGuardOperationResult<MacOSPendingPowerStateBackupState>.Success(state);
    }

    public static LidGuardOperationResult Restore(MacOSPendingPowerStateBackupState state)
    {
        if (state is null) return LidGuardOperationResult.Success();

        if (state.IncludesHibernateMode)
        {
            var restoreResult = MacOSPowerSettings.SetHibernateMode(state.HibernateMode);
            if (!restoreResult.Succeeded) return restoreResult;
        }

        if (MacOSPendingPowerStateBackupStore.TryDelete(out var deleteMessage)) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(deleteMessage);
    }

    public static LidGuardOperationResult RollBackFailedApply(
        MacOSPendingPowerStateBackupState state,
        LidGuardOperationResult applyResult)
    {
        var restoreResult = Restore(state);
        if (!restoreResult.Succeeded)
        {
            var message = $"{CreateResultMessage(applyResult)} Rollback failed: {CreateResultMessage(restoreResult)}";
            return LidGuardOperationResult.Failure(message, GetNativeErrorCode(applyResult, restoreResult));
        }

        return LidGuardOperationResult.Failure(CreateResultMessage(applyResult), applyResult.NativeErrorCode);
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

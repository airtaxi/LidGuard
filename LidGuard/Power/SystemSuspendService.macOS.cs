using LidGuard.Results;
using LidGuard.Runtime;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class SystemSuspendService : ISystemSuspendService
{
    private static readonly TimeSpan s_hibernateRestoreDelay = TimeSpan.FromSeconds(5);

    public LidGuardOperationResult Suspend(SystemSuspendMode suspendMode)
    {
        if (suspendMode != SystemSuspendMode.Hibernate) return MacOSPowerSettings.SleepNow();

        var restorePendingResult = MacOSPendingPowerStateBackupManager.RestorePendingBackupIfPresent();
        if (!restorePendingResult.Succeeded) return LidGuardOperationResult.Failure(restorePendingResult.Message, restorePendingResult.NativeErrorCode);

        var hibernateModeResult = MacOSPowerSettings.ReadHibernateMode();
        if (!hibernateModeResult.Succeeded) return LidGuardOperationResult.Failure(hibernateModeResult.Message, hibernateModeResult.NativeErrorCode);
        if (hibernateModeResult.Value == MacOSPowerSettings.HibernateModeDiskOnly) return MacOSPowerSettings.SleepNow();

        var captureResult = MacOSPendingPowerStateBackupManager.CaptureHibernateMode();
        if (!captureResult.Succeeded) return LidGuardOperationResult.Failure(captureResult.Message, captureResult.NativeErrorCode);

        var applyResult = MacOSPowerSettings.SetHibernateMode(MacOSPowerSettings.HibernateModeDiskOnly);
        if (!applyResult.Succeeded) return MacOSPendingPowerStateBackupManager.RollBackFailedApply(captureResult.Value, applyResult);

        var sleepResult = MacOSPowerSettings.SleepNow();
        if (sleepResult.Succeeded) Thread.Sleep(s_hibernateRestoreDelay);

        var restoreResult = MacOSPendingPowerStateBackupManager.Restore(captureResult.Value);
        if (!sleepResult.Succeeded && !restoreResult.Succeeded)
        {
            var message = $"{CreateResultMessage(sleepResult)} Restore failed: {CreateResultMessage(restoreResult)}";
            return LidGuardOperationResult.Failure(message, GetNativeErrorCode(sleepResult, restoreResult));
        }

        if (!sleepResult.Succeeded) return sleepResult;
        if (!restoreResult.Succeeded) return restoreResult;

        return LidGuardOperationResult.Success();
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

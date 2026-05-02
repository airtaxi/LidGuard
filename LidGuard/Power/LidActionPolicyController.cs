using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Settings;

namespace LidGuard.Power;

public sealed class LidActionPolicyController(ILidActionService lidActionService)
{
    public LidGuardOperationResult<LidActionBackup> CaptureBackup(LidGuardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var powerSchemeResult = lidActionService.GetActivePowerSchemeIdentifier();
        if (!powerSchemeResult.Succeeded) return LidGuardOperationResult<LidActionBackup>.Failure(powerSchemeResult.Message, powerSchemeResult.NativeErrorCode);

        var includesPowerLines = settings.ChangeLidAction;
        var includesAlternatingCurrent = includesPowerLines;
        var includesDirectCurrent = includesPowerLines;
        if (!includesAlternatingCurrent && !includesDirectCurrent)
        {
            var emptyBackup = new LidActionBackup(powerSchemeResult.Value, false, LidAction.DoNothing, false, LidAction.DoNothing);
            return LidGuardOperationResult<LidActionBackup>.Success(emptyBackup);
        }

        var alternatingCurrentAction = LidAction.DoNothing;
        var directCurrentAction = LidAction.DoNothing;

        if (includesAlternatingCurrent)
        {
            var readResult = lidActionService.ReadLidAction(powerSchemeResult.Value, PowerLine.AlternatingCurrent);
            if (!readResult.Succeeded) return LidGuardOperationResult<LidActionBackup>.Failure(readResult.Message, readResult.NativeErrorCode);
            alternatingCurrentAction = readResult.Value;
        }

        if (includesDirectCurrent)
        {
            var readResult = lidActionService.ReadLidAction(powerSchemeResult.Value, PowerLine.DirectCurrent);
            if (!readResult.Succeeded) return LidGuardOperationResult<LidActionBackup>.Failure(readResult.Message, readResult.NativeErrorCode);
            directCurrentAction = readResult.Value;
        }

        var backup = new LidActionBackup(
            powerSchemeResult.Value,
            includesAlternatingCurrent,
            alternatingCurrentAction,
            includesDirectCurrent,
            directCurrentAction);

        return LidGuardOperationResult<LidActionBackup>.Success(backup);
    }

    public LidGuardOperationResult ApplyTemporaryDoNothing(LidActionBackup backup)
    {
        if (backup.IncludesAlternatingCurrent)
        {
            var writeResult = lidActionService.WriteLidAction(backup.PowerSchemeIdentifier, PowerLine.AlternatingCurrent, LidAction.DoNothing);
            if (!writeResult.Succeeded) return writeResult;
        }

        if (backup.IncludesDirectCurrent)
        {
            var writeResult = lidActionService.WriteLidAction(backup.PowerSchemeIdentifier, PowerLine.DirectCurrent, LidAction.DoNothing);
            if (!writeResult.Succeeded) return writeResult;
        }

        return lidActionService.ApplyPowerScheme(backup.PowerSchemeIdentifier);
    }

    public LidGuardOperationResult<LidActionBackup> ApplyTemporaryDoNothing(LidGuardSettings settings)
    {
        var captureResult = CaptureBackup(settings);
        if (!captureResult.Succeeded) return captureResult;

        var applyResult = ApplyTemporaryDoNothing(captureResult.Value);
        if (!applyResult.Succeeded) return LidGuardOperationResult<LidActionBackup>.Failure(applyResult.Message, applyResult.NativeErrorCode);

        return LidGuardOperationResult<LidActionBackup>.Success(captureResult.Value);
    }

    public LidGuardOperationResult Restore(LidActionBackup backup)
    {
        if (backup.IncludesAlternatingCurrent)
        {
            var writeResult = lidActionService.WriteLidAction(backup.PowerSchemeIdentifier, PowerLine.AlternatingCurrent, backup.AlternatingCurrentAction);
            if (!writeResult.Succeeded) return writeResult;
        }

        if (backup.IncludesDirectCurrent)
        {
            var writeResult = lidActionService.WriteLidAction(backup.PowerSchemeIdentifier, PowerLine.DirectCurrent, backup.DirectCurrentAction);
            if (!writeResult.Succeeded) return writeResult;
        }

        return lidActionService.ApplyPowerScheme(backup.PowerSchemeIdentifier);
    }
}

using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class LidActionService : ILidActionService
{
    private static readonly Guid s_macOSLidActionSchemeIdentifier = new("eb120717-8275-41c2-9400-162dc42ef4ca");
    private LidAction _alternatingCurrentLidAction = LidAction.Sleep;
    private LidAction _directCurrentLidAction = LidAction.Sleep;

    public LidGuardOperationResult<Guid> GetActivePowerSchemeIdentifier()
        => LidGuardOperationResult<Guid>.Success(s_macOSLidActionSchemeIdentifier);

    public LidGuardOperationResult<LidAction> ReadLidAction(Guid powerSchemeIdentifier, PowerLine powerLine)
    {
        if (powerSchemeIdentifier != s_macOSLidActionSchemeIdentifier) return LidGuardOperationResult<LidAction>.Failure("The macOS lid action scheme identifier is invalid.");

        var sleepDisabledResult = MacOSPowerSettings.ReadSleepDisabled();
        if (!sleepDisabledResult.Succeeded) return LidGuardOperationResult<LidAction>.Failure(sleepDisabledResult.Message, sleepDisabledResult.NativeErrorCode);

        return LidGuardOperationResult<LidAction>.Success(sleepDisabledResult.Value ? LidAction.DoNothing : LidAction.Sleep);
    }

    public LidGuardOperationResult WriteLidAction(Guid powerSchemeIdentifier, PowerLine powerLine, LidAction lidAction)
    {
        if (powerSchemeIdentifier != s_macOSLidActionSchemeIdentifier) return LidGuardOperationResult.Failure("The macOS lid action scheme identifier is invalid.");

        if (powerLine == PowerLine.AlternatingCurrent) _alternatingCurrentLidAction = lidAction;
        if (powerLine == PowerLine.DirectCurrent) _directCurrentLidAction = lidAction;
        return LidGuardOperationResult.Success();
    }

    public LidGuardOperationResult ApplyPowerScheme(Guid powerSchemeIdentifier)
    {
        if (powerSchemeIdentifier != s_macOSLidActionSchemeIdentifier) return LidGuardOperationResult.Failure("The macOS lid action scheme identifier is invalid.");

        var shouldDisableSleep = _alternatingCurrentLidAction == LidAction.DoNothing && _directCurrentLidAction == LidAction.DoNothing;
        return MacOSPowerSettings.SetSleepDisabled(shouldDisableSleep);
    }
}

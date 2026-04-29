using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;

namespace LidGuardLib.Commons.Services;

public interface ILidActionService
{
    LidGuardOperationResult<Guid> GetActivePowerSchemeIdentifier();

    LidGuardOperationResult<LidAction> ReadLidAction(Guid powerSchemeIdentifier, PowerLine powerLine);

    LidGuardOperationResult WriteLidAction(Guid powerSchemeIdentifier, PowerLine powerLine, LidAction lidAction);

    LidGuardOperationResult ApplyPowerScheme(Guid powerSchemeIdentifier);
}

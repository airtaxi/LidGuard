using LidGuard.Power;
using LidGuard.Results;

namespace LidGuard.Services;

public interface ILidActionService
{
    LidGuardOperationResult<Guid> GetActivePowerSchemeIdentifier();

    LidGuardOperationResult<LidAction> ReadLidAction(Guid powerSchemeIdentifier, PowerLine powerLine);

    LidGuardOperationResult WriteLidAction(Guid powerSchemeIdentifier, PowerLine powerLine, LidAction lidAction);

    LidGuardOperationResult ApplyPowerScheme(Guid powerSchemeIdentifier);
}

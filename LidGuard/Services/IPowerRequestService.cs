using LidGuard.Power;
using LidGuard.Results;

namespace LidGuard.Services;

public interface IPowerRequestService
{
    LidGuardOperationResult<ILidGuardPowerRequest> Create(PowerRequestOptions options);
}

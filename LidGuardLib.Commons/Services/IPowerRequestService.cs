using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;

namespace LidGuardLib.Commons.Services;

public interface IPowerRequestService
{
    LidGuardOperationResult<ILidGuardPowerRequest> Create(PowerRequestOptions options);
}

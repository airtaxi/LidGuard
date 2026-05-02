using LidGuard.Power;
using LidGuard.Results;

namespace LidGuard.Services;

public interface ISystemSuspendService
{
    LidGuardOperationResult Suspend(SystemSuspendMode suspendMode);
}

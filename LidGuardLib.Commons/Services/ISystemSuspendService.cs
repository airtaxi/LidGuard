using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;

namespace LidGuardLib.Commons.Services;

public interface ISystemSuspendService
{
    LidGuardOperationResult Suspend(SystemSuspendMode suspendMode);
}

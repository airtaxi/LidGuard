using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Services;

namespace LidGuardLib.Commons.Platform;

public sealed class LidGuardRuntimeServiceSet(
    IPowerRequestService powerRequestService,
    ICommandLineProcessResolver commandLineProcessResolver,
    IProcessExitWatcher processExitWatcher,
    LidActionPolicyController lidActionPolicyController,
    ISystemSuspendService systemSuspendService,
    ILidStateSource lidStateSource) : IDisposable
{
    public IPowerRequestService PowerRequestService { get; } = powerRequestService;

    public ICommandLineProcessResolver CommandLineProcessResolver { get; } = commandLineProcessResolver;

    public IProcessExitWatcher ProcessExitWatcher { get; } = processExitWatcher;

    public LidActionPolicyController LidActionPolicyController { get; } = lidActionPolicyController;

    public ISystemSuspendService SystemSuspendService { get; } = systemSuspendService;

    public ILidStateSource LidStateSource { get; } = lidStateSource;

    public void Dispose()
    {
        if (LidStateSource is IDisposable disposableLidStateSource) disposableLidStateSource.Dispose();
    }
}

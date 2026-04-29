using System.Runtime.Versioning;
using LidGuardLib.Commons.Platform;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Windows.Power;
using LidGuardLib.Windows.Processes;

namespace LidGuardLib.Windows.Platform;

public sealed class WindowsLidGuardRuntimePlatform : ILidGuardRuntimePlatform
{
    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(6, 1);

    public string UnsupportedMessage => "LidGuard currently supports Windows only. macOS and Linux support is planned.";

    public LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(UnsupportedMessage);

        var lidActionService = new WindowsLidActionService();
        var lidStateSource = CreateLidStateSource();
        var serviceSet = new LidGuardRuntimeServiceSet(
            new WindowsPowerRequestService(),
            new WindowsCommandLineProcessResolver(),
            new WindowsProcessExitWatcher(),
            new LidActionPolicyController(lidActionService),
            new WindowsSystemSuspendService(),
            lidStateSource);

        return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Success(serviceSet);
    }

    [SupportedOSPlatform("windows6.1")]
    private static ILidStateSource CreateLidStateSource() => new WindowsLidStateSource();
}

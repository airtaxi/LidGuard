using System.Runtime.Versioning;
using LidGuardLib.Commons.Platform;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Power;
using LidGuardLib.Processes;

namespace LidGuardLib.Platform;

public sealed class LidGuardRuntimePlatform : ILidGuardRuntimePlatform
{
    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(6, 1);

    public string UnsupportedMessage => "LidGuard currently supports Windows only. macOS and Linux support is planned.";

    public LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(UnsupportedMessage);

        var postStopSuspendSoundPlayerResult = CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(postStopSuspendSoundPlayerResult.Message);

        var lidActionService = new LidActionService();
        var lidStateSource = CreateLidStateSource();
        var serviceSet = new LidGuardRuntimeServiceSet(
            new PowerRequestService(),
            new CommandLineProcessResolver(),
            new ProcessExitWatcher(),
            new LidActionPolicyController(lidActionService),
            new SystemSuspendService(),
            postStopSuspendSoundPlayerResult.Value,
            lidStateSource,
            new VisibleDisplayMonitorCountProvider());

        return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Success(serviceSet);
    }

    public LidGuardOperationResult<IPostStopSuspendSoundPlayer> CreatePostStopSuspendSoundPlayer()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Failure(UnsupportedMessage);
        return LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Success(new PostStopSuspendSoundPlayer());
    }

    [SupportedOSPlatform("windows6.1")]
    private static ILidStateSource CreateLidStateSource() => new LidStateSource();
}

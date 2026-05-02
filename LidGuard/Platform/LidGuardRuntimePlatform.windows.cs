using System.Runtime.Versioning;
using LidGuard.Audio;
using LidGuard.Platform;
using LidGuard.Power;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Processes;

namespace LidGuard.Platform;

public sealed class LidGuardRuntimePlatform : ILidGuardRuntimePlatform
{
    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(6, 1);

    public string UnsupportedMessage => "This LidGuard build requires Windows 7 or later.";

    public LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(UnsupportedMessage);

        var postStopSuspendSoundPlayerResult = CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(postStopSuspendSoundPlayerResult.Message);

        var systemAudioVolumeControllerResult = CreateSystemAudioVolumeController();
        if (!systemAudioVolumeControllerResult.Succeeded) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(systemAudioVolumeControllerResult.Message);

        var lidActionService = new LidActionService();
        var lidStateSource = CreateLidStateSource();
        var serviceSet = new LidGuardRuntimeServiceSet(
            new PowerRequestService(),
            new CommandLineProcessResolver(),
            new ProcessExitWatcher(),
            new LidActionPolicyController(lidActionService),
            new SystemSuspendService(),
            postStopSuspendSoundPlayerResult.Value,
            systemAudioVolumeControllerResult.Value,
            lidStateSource,
            new VisibleDisplayMonitorCountProvider());

        return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Success(serviceSet);
    }

    public LidGuardOperationResult<IPostStopSuspendSoundPlayer> CreatePostStopSuspendSoundPlayer()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Failure(UnsupportedMessage);
        return LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Success(new PostStopSuspendSoundPlayer());
    }

    public LidGuardOperationResult<ISystemAudioVolumeController> CreateSystemAudioVolumeController()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) return LidGuardOperationResult<ISystemAudioVolumeController>.Failure(UnsupportedMessage);
        return LidGuardOperationResult<ISystemAudioVolumeController>.Success(new SystemAudioVolumeController());
    }

    [SupportedOSPlatform("windows6.1")]
    private static ILidStateSource CreateLidStateSource() => new LidStateSource();
}

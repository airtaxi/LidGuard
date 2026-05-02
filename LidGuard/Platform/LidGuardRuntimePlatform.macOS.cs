using LidGuard.Audio;
using LidGuard.Power;
using LidGuard.Processes;
using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Platform;

public sealed class LidGuardRuntimePlatform : ILidGuardRuntimePlatform
{
    public bool IsSupported => OperatingSystem.IsMacOS();

    public string UnsupportedMessage => "This LidGuard build requires macOS.";

    public LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet()
    {
        if (!OperatingSystem.IsMacOS()) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(UnsupportedMessage);

        var postStopSuspendSoundPlayerResult = CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(postStopSuspendSoundPlayerResult.Message);

        var systemAudioVolumeControllerResult = CreateSystemAudioVolumeController();
        if (!systemAudioVolumeControllerResult.Succeeded) return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(systemAudioVolumeControllerResult.Message);

        var lidActionService = new LidActionService();
        var serviceSet = new LidGuardRuntimeServiceSet(
            new PowerRequestService(),
            new CommandLineProcessResolver(),
            new ProcessExitWatcher(),
            new LidActionPolicyController(lidActionService),
            new SystemSuspendService(),
            postStopSuspendSoundPlayerResult.Value,
            systemAudioVolumeControllerResult.Value,
            new LidStateSource(),
            new VisibleDisplayMonitorCountProvider());

        return LidGuardOperationResult<LidGuardRuntimeServiceSet>.Success(serviceSet);
    }

    public LidGuardOperationResult<IPostStopSuspendSoundPlayer> CreatePostStopSuspendSoundPlayer()
        => OperatingSystem.IsMacOS()
            ? LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Success(new PostStopSuspendSoundPlayer())
            : LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Failure(UnsupportedMessage);

    public LidGuardOperationResult<ISystemAudioVolumeController> CreateSystemAudioVolumeController()
        => OperatingSystem.IsMacOS()
            ? LidGuardOperationResult<ISystemAudioVolumeController>.Success(new SystemAudioVolumeController())
            : LidGuardOperationResult<ISystemAudioVolumeController>.Failure(UnsupportedMessage);
}

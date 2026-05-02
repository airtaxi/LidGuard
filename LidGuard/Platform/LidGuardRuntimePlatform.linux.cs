using LidGuard.Audio;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Power;
using LidGuard.Processes;

namespace LidGuard.Platform;

public sealed class LidGuardRuntimePlatform : ILidGuardRuntimePlatform
{
    public bool IsSupported => OperatingSystem.IsLinux();

    public string UnsupportedMessage => "LidGuard Linux support requires a systemd/logind environment. macOS support is planned.";

    public LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet()
    {
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
        => LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Success(new PostStopSuspendSoundPlayer());

    public LidGuardOperationResult<ISystemAudioVolumeController> CreateSystemAudioVolumeController()
        => LidGuardOperationResult<ISystemAudioVolumeController>.Success(new SystemAudioVolumeController());
}

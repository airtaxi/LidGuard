using LidGuard.Platform;
using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Platform;

public sealed class LidGuardRuntimePlatform : ILidGuardRuntimePlatform
{
    public bool IsSupported => false;

    public string UnsupportedMessage => "LidGuard currently supports Windows only. macOS and Linux support is planned.";

    public LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet()
        => LidGuardOperationResult<LidGuardRuntimeServiceSet>.Failure(UnsupportedMessage);

    public LidGuardOperationResult<IPostStopSuspendSoundPlayer> CreatePostStopSuspendSoundPlayer()
        => LidGuardOperationResult<IPostStopSuspendSoundPlayer>.Failure(UnsupportedMessage);

    public LidGuardOperationResult<ISystemAudioVolumeController> CreateSystemAudioVolumeController()
        => LidGuardOperationResult<ISystemAudioVolumeController>.Failure(UnsupportedMessage);
}

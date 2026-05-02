using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Platform;

public interface ILidGuardRuntimePlatform
{
    bool IsSupported { get; }

    string UnsupportedMessage { get; }

    LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet();

    LidGuardOperationResult<IPostStopSuspendSoundPlayer> CreatePostStopSuspendSoundPlayer();

    LidGuardOperationResult<ISystemAudioVolumeController> CreateSystemAudioVolumeController();
}

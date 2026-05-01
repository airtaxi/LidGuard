using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;

namespace LidGuardLib.Commons.Platform;

public interface ILidGuardRuntimePlatform
{
    bool IsSupported { get; }

    string UnsupportedMessage { get; }

    LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet();

    LidGuardOperationResult<IPostStopSuspendSoundPlayer> CreatePostStopSuspendSoundPlayer();

    LidGuardOperationResult<ISystemAudioVolumeController> CreateSystemAudioVolumeController();
}

using LidGuard.Results;

namespace LidGuard.Services;

public interface ISystemAudioVolumeController
{
    LidGuardOperationResult<SystemAudioVolumeState> CaptureDefaultRenderDeviceState();

    LidGuardOperationResult ApplyDefaultRenderDeviceVolumeOverride(int volumeOverridePercent);

    LidGuardOperationResult RestoreDefaultRenderDeviceState(SystemAudioVolumeState state);
}

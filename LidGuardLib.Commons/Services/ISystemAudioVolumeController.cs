using LidGuardLib.Commons.Results;

namespace LidGuardLib.Commons.Services;

public interface ISystemAudioVolumeController
{
    LidGuardOperationResult<SystemAudioVolumeState> CaptureDefaultRenderDeviceState();

    LidGuardOperationResult ApplyDefaultRenderDeviceVolumeOverride(int volumeOverridePercent);

    LidGuardOperationResult RestoreDefaultRenderDeviceState(SystemAudioVolumeState state);
}

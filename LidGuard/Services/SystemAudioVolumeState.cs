namespace LidGuard.Services;

public sealed class SystemAudioVolumeState
{
    public float MasterVolumeScalar { get; init; }

    public bool IsMuted { get; init; }
}

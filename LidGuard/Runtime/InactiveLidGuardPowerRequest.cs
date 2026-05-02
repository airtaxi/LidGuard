using LidGuard.Services;

namespace LidGuard.Runtime;

internal sealed class InactiveLidGuardPowerRequest : ILidGuardPowerRequest
{
    public static InactiveLidGuardPowerRequest Instance { get; } = new();

    public bool IsActive => false;

    public void Dispose()
    {
    }
}


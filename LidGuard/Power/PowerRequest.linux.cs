using LidGuard.Services;

namespace LidGuard.Power;

internal sealed class PowerRequest(SystemdInhibitor inhibitor) : ILidGuardPowerRequest
{
    public bool IsActive => inhibitor.IsActive;

    public void Dispose() => inhibitor.Dispose();
}

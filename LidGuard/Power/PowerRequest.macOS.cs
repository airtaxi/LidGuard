using LidGuard.Services;

namespace LidGuard.Power;

internal sealed class PowerRequest(CaffeinateAssertion assertion) : ILidGuardPowerRequest
{
    public bool IsActive => assertion.IsActive;

    public void Dispose() => assertion.Dispose();
}

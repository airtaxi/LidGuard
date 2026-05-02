using LidGuard.Ipc;
using LidGuard.Power;

namespace LidGuard.Hooks;

internal static class ClosedLidPolicyStatus
{
    public static bool IsActive(LidGuardPipeResponse response)
        => response.LidSwitchState == LidSwitchState.Closed && response.VisibleDisplayMonitorCount <= 0;

    public static string DescribeInactiveReason(LidGuardPipeResponse response)
    {
        if (response.LidSwitchState != LidSwitchState.Closed) return $"the lid state is {response.LidSwitchState}";
        if (response.VisibleDisplayMonitorCount > 0)
            return $"{response.VisibleDisplayMonitorCount} visible display monitor(s) are active while the lid is closed";
        return "the closed-lid policy is not active";
    }
}

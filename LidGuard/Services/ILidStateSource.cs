using LidGuard.Power;

namespace LidGuard.Services;

public interface ILidStateSource
{
    LidSwitchState CurrentState { get; }
}

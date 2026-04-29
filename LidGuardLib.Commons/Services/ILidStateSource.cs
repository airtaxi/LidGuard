using LidGuardLib.Commons.Power;

namespace LidGuardLib.Commons.Services;

public interface ILidStateSource
{
    LidSwitchState CurrentState { get; }
}

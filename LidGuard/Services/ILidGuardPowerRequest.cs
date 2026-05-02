namespace LidGuard.Services;

public interface ILidGuardPowerRequest : IDisposable
{
    bool IsActive { get; }
}

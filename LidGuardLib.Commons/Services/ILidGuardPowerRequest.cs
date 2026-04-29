namespace LidGuardLib.Commons.Services;

public interface ILidGuardPowerRequest : IDisposable
{
    bool IsActive { get; }
}

using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class LidActionService : ILidActionService, IDisposable
{
    private static readonly Guid s_linuxLidActionSchemeIdentifier = new("1d4c92cb-9a9a-4d20-b89c-111ea5270e91");
    private SystemdInhibitor _lidSwitchInhibitor;
    private LidAction _alternatingCurrentLidAction = LidAction.Sleep;
    private LidAction _directCurrentLidAction = LidAction.Sleep;

    public LidGuardOperationResult<Guid> GetActivePowerSchemeIdentifier()
        => LidGuardOperationResult<Guid>.Success(s_linuxLidActionSchemeIdentifier);

    public LidGuardOperationResult<LidAction> ReadLidAction(Guid powerSchemeIdentifier, PowerLine powerLine)
    {
        if (powerSchemeIdentifier != s_linuxLidActionSchemeIdentifier) return LidGuardOperationResult<LidAction>.Failure("The Linux lid action scheme identifier is invalid.");
        return LidGuardOperationResult<LidAction>.Success(_lidSwitchInhibitor is not null && _lidSwitchInhibitor.IsActive ? LidAction.DoNothing : LidAction.Sleep);
    }

    public LidGuardOperationResult WriteLidAction(Guid powerSchemeIdentifier, PowerLine powerLine, LidAction lidAction)
    {
        if (powerSchemeIdentifier != s_linuxLidActionSchemeIdentifier) return LidGuardOperationResult.Failure("The Linux lid action scheme identifier is invalid.");

        if (powerLine == PowerLine.AlternatingCurrent) _alternatingCurrentLidAction = lidAction;
        if (powerLine == PowerLine.DirectCurrent) _directCurrentLidAction = lidAction;
        return LidGuardOperationResult.Success();
    }

    public LidGuardOperationResult ApplyPowerScheme(Guid powerSchemeIdentifier)
    {
        if (powerSchemeIdentifier != s_linuxLidActionSchemeIdentifier) return LidGuardOperationResult.Failure("The Linux lid action scheme identifier is invalid.");

        if (_alternatingCurrentLidAction == LidAction.DoNothing && _directCurrentLidAction == LidAction.DoNothing) return EnsureLidSwitchInhibitor();

        DisposeLidSwitchInhibitor();
        return LidGuardOperationResult.Success();
    }

    public void Dispose() => DisposeLidSwitchInhibitor();

    private LidGuardOperationResult EnsureLidSwitchInhibitor()
    {
        if (_lidSwitchInhibitor is not null && _lidSwitchInhibitor.IsActive) return LidGuardOperationResult.Success();

        DisposeLidSwitchInhibitor();
        var inhibitorResult = SystemdInhibitor.TryAcquire(
            "handle-lid-switch",
            "LidGuard is temporarily inhibiting lid-close handling while an agent session is running.");
        if (!inhibitorResult.Succeeded) return LidGuardOperationResult.Failure(inhibitorResult.Message, inhibitorResult.NativeErrorCode);

        _lidSwitchInhibitor = inhibitorResult.Value;
        return LidGuardOperationResult.Success();
    }

    private void DisposeLidSwitchInhibitor()
    {
        _lidSwitchInhibitor?.Dispose();
        _lidSwitchInhibitor = null;
    }
}

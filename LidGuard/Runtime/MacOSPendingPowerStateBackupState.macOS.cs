namespace LidGuard.Runtime;

internal sealed class MacOSPendingPowerStateBackupState
{
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IncludesHibernateMode { get; init; }

    public int HibernateMode { get; init; }
}

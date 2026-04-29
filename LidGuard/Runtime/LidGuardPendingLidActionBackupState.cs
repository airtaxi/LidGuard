using LidGuardLib.Commons.Power;

namespace LidGuard.Runtime;

internal sealed class LidGuardPendingLidActionBackupState
{
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;

    public Guid PowerSchemeIdentifier { get; init; }

    public bool IncludesAlternatingCurrent { get; init; }

    public LidAction AlternatingCurrentAction { get; init; } = LidAction.DoNothing;

    public bool IncludesDirectCurrent { get; init; }

    public LidAction DirectCurrentAction { get; init; } = LidAction.DoNothing;

    public LidActionBackup ToBackup() => new(
        PowerSchemeIdentifier,
        IncludesAlternatingCurrent,
        AlternatingCurrentAction,
        IncludesDirectCurrent,
        DirectCurrentAction);

    public static LidGuardPendingLidActionBackupState Create(LidActionBackup backup) => new()
    {
        PowerSchemeIdentifier = backup.PowerSchemeIdentifier,
        IncludesAlternatingCurrent = backup.IncludesAlternatingCurrent,
        AlternatingCurrentAction = backup.AlternatingCurrentAction,
        IncludesDirectCurrent = backup.IncludesDirectCurrent,
        DirectCurrentAction = backup.DirectCurrentAction
    };
}

using LidGuard.Settings;
using LidGuard.Commands;

namespace LidGuard.Control;

public sealed class LidGuardSettingsUpdateOutcome
{
    public bool ResetToDefaults { get; init; }

    public bool HadEffectiveChanges { get; init; }

    public string[] AppliedChanges { get; init; } = [];

    public LidGuardSettings PreviousStoredSettings { get; init; } = LidGuardSettings.Default;

    public LidGuardSettings UpdatedStoredSettings { get; init; } = LidGuardSettings.Default;

    public ManagedHookStatusMessageRefreshResult ManagedHookRefreshResult { get; init; }

    public LidGuardControlSnapshot Snapshot { get; init; } = new();
}

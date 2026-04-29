using System.ComponentModel;
using LidGuardLib.Commons.Settings;
using LidGuard.Control;

namespace LidGuard.Mcp.Models;

public sealed class LidGuardSettingsUpdateToolResponse
{
    [Description("Human-readable summary of the applied change set and runtime synchronization result.")]
    public string Summary { get; init; } = string.Empty;

    [Description("Indicates whether the update started from LidGuard's headless runtime defaults before applying individual changes.")]
    public bool ResetToDefaults { get; init; }

    [Description("Indicates whether the requested update produced any effective value changes.")]
    public bool HadEffectiveChanges { get; init; }

    [Description("Settings field names that changed after the update.")]
    public string[] AppliedChanges { get; init; } = [];

    [Description("The stored settings before the update was applied.")]
    public LidGuardSettings PreviousStoredSettings { get; init; } = LidGuardSettings.Default;

    [Description("The stored settings after the update was applied.")]
    public LidGuardSettings UpdatedStoredSettings { get; init; } = LidGuardSettings.Default;

    [Description("Stored LidGuard settings and the current runtime status snapshot after the update attempt.")]
    public LidGuardControlSnapshot Snapshot { get; init; } = new();
}

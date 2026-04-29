using System.ComponentModel;
using LidGuard.Control;

namespace LidGuard.Mcp.Models;

public sealed class LidGuardSettingsStatusToolResponse
{
    [Description("Human-readable summary of the settings and runtime state.")]
    public string Summary { get; init; } = string.Empty;

    [Description("Stored LidGuard settings and the current runtime status snapshot.")]
    public LidGuardControlSnapshot Snapshot { get; init; } = new();
}

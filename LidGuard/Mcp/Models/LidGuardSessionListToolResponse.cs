using System.ComponentModel;
using LidGuard.Ipc;
using LidGuardLib.Commons.Power;

namespace LidGuard.Mcp.Models;

public sealed class LidGuardSessionListToolResponse
{
    [Description("Human-readable summary of the current runtime session list state.")]
    public string Summary { get; init; } = string.Empty;

    [Description("Indicates whether the LidGuard runtime was reachable while listing sessions.")]
    public bool RuntimeReachable { get; init; }

    [Description("Indicates whether the runtime is simply not running, instead of failing for another reason.")]
    public bool RuntimeUnavailable { get; init; }

    [Description("Additional runtime status detail from LidGuard.")]
    public string RuntimeMessage { get; init; } = string.Empty;

    [Description("The number of active sessions reported by the runtime.")]
    public int ActiveSessionCount { get; init; }

    [Description("The current lid switch state reported by the runtime.")]
    public LidSwitchState LidSwitchState { get; init; } = LidSwitchState.Unknown;

    [Description("The number of visible display monitors currently reported by the runtime.")]
    public int VisibleDisplayMonitorCount { get; init; }

    [Description("The active LidGuard sessions currently tracked by the runtime.")]
    public LidGuardSessionStatus[] Sessions { get; init; } = [];
}

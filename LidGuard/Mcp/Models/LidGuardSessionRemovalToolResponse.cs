using System.ComponentModel;
using LidGuard.Control;
using LidGuard.Ipc;
using LidGuardLib.Commons.Sessions;

namespace LidGuard.Mcp.Models;

public sealed class LidGuardSessionRemovalToolResponse
{
    [Description("Human-readable summary of the requested session removal and the resulting runtime state.")]
    public string Summary { get; init; } = string.Empty;

    [Description("The session identifier that was requested for removal.")]
    public string RequestedSessionIdentifier { get; init; } = string.Empty;

    [Description("Indicates whether the removal request was narrowed to one provider.")]
    public bool HasProviderFilter { get; init; }

    [Description("The provider filter that was requested when HasProviderFilter is true.")]
    public AgentProvider RequestedProvider { get; init; } = AgentProvider.Unknown;

    [Description("Indicates whether the removal request was narrowed to one provider name within the selected provider.")]
    public bool HasProviderNameFilter { get; init; }

    [Description("The provider name filter that was requested when HasProviderNameFilter is true.")]
    public string RequestedProviderName { get; init; } = string.Empty;

    [Description("Active sessions that matched the request before the removal command ran.")]
    public LidGuardSessionStatus[] RemovedSessions { get; init; } = [];

    [Description("Stored LidGuard settings and the current runtime status snapshot after the removal attempt.")]
    public LidGuardControlSnapshot Snapshot { get; init; } = new();
}

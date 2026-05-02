using System.ComponentModel;
using LidGuard.Control;
using LidGuard.Sessions;

namespace LidGuard.Mcp.Models;

public sealed class LidGuardSessionCommandToolResponse
{
    [Description("Human-readable summary of the requested LidGuard session command and the resulting runtime state.")]
    public string Summary { get; init; } = string.Empty;

    [Description("The LidGuard runtime command that was requested.")]
    public string RequestedCommand { get; init; } = string.Empty;

    [Description("The session identifier that was targeted by the command.")]
    public string RequestedSessionIdentifier { get; init; } = string.Empty;

    [Description("The exact session identifier value to reuse verbatim in later LidGuard session tools for the same ongoing session.")]
    public string SessionIdentifierToReuse { get; init; } = string.Empty;

    [Description("The provider that was targeted by the command.")]
    public AgentProvider RequestedProvider { get; init; } = AgentProvider.Unknown;

    [Description("The provider name that was targeted when RequestedProvider is mcp.")]
    public string RequestedProviderName { get; init; } = string.Empty;

    [Description("Stored LidGuard settings and the current runtime status snapshot after the command.")]
    public LidGuardControlSnapshot Snapshot { get; init; } = new();
}

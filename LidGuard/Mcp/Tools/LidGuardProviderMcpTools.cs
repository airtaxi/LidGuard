using System.ComponentModel;
using LidGuard.Control;
using LidGuard.Mcp.Models;
using LidGuardLib.Commons.Sessions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LidGuard.Mcp.Tools;

[McpServerToolType]
public sealed class LidGuardProviderMcpTools(
    ProviderMcpServerConfiguration providerMcpServerConfiguration,
    LidGuardControlService controlService)
{
    [McpServerTool(
        Name = "provider_start_session",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Call this before you start processing a new user prompt for this MCP-managed provider session. Use the provider's own session id when available. If the provider does not expose one, generate a stable session id yourself and keep reusing it until you truly stop the session.")]
    public async Task<LidGuardSessionCommandToolResponse> StartSession(
        [Description("The provider session identifier to keep using across turns until the task is truly complete.")]
        string sessionIdentifier,
        [Description("Optional working directory for the current task. Pass it when the provider can expose the active project folder.")]
        string workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.StartSessionAsync(
            sessionIdentifier,
            AgentProvider.Mcp,
            providerMcpServerConfiguration.ProviderName,
            workingDirectory ?? string.Empty,
            0,
            cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return CreateSessionCommandToolResponse(result.Value);
    }

    [McpServerTool(
        Name = "provider_stop_session",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Call this before the turn ends only when the work is truly complete and this session no longer needs LidGuard protection. If you are ending the turn because you need the user's next input, prefer provider_set_soft_lock instead.")]
    public async Task<LidGuardSessionCommandToolResponse> StopSession(
        [Description("The provider session identifier that is truly finished and can be stopped.")]
        string sessionIdentifier,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.StopSessionAsync(
            sessionIdentifier,
            AgentProvider.Mcp,
            providerMcpServerConfiguration.ProviderName,
            cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return CreateSessionCommandToolResponse(result.Value);
    }

    [McpServerTool(
        Name = "provider_set_soft_lock",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Call this before you finish a turn because you need the user's next input and want LidGuard to allow suspend. A soft-locked session stays tracked, but it stops keeping the machine awake. This tool cannot force the turn to end; after calling it, you still need to end or hand back the conversation yourself.")]
    public async Task<LidGuardSessionCommandToolResponse> SetSoftLock(
        [Description("The provider session identifier that should stay tracked but become suspend-eligible.")]
        string sessionIdentifier,
        [Description("The reason for the soft lock, such as waiting_for_user_input.")]
        string reason,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.SetSessionSoftLockAsync(
            sessionIdentifier,
            AgentProvider.Mcp,
            providerMcpServerConfiguration.ProviderName,
            reason,
            cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return CreateSessionCommandToolResponse(result.Value);
    }

    [McpServerTool(
        Name = "provider_clear_soft_lock",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Call this when the provider session can resume autonomous work after a previous soft lock, typically right before you continue work with the same session id on a later turn.")]
    public async Task<LidGuardSessionCommandToolResponse> ClearSoftLock(
        [Description("The provider session identifier whose previous soft lock should be cleared.")]
        string sessionIdentifier,
        [Description("Optional reason describing why the session can resume work, such as resumed_after_user_reply.")]
        string reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.ClearSessionSoftLockAsync(
            sessionIdentifier,
            AgentProvider.Mcp,
            providerMcpServerConfiguration.ProviderName,
            reason,
            cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return CreateSessionCommandToolResponse(result.Value);
    }

    private static LidGuardSessionCommandToolResponse CreateSessionCommandToolResponse(LidGuardSessionCommandOutcome outcome)
    {
        var scope = $"{AgentProviderDisplay.CreateProviderDisplayText(outcome.RequestedProvider, outcome.RequestedProviderName)}:{outcome.RequestedSessionIdentifier}";
        var summary = outcome.Snapshot.RuntimeReachable
            ? $"{outcome.RuntimeMessage} Runtime now tracks {outcome.Snapshot.ActiveSessionCount} active session(s) after handling {scope}."
            : outcome.Snapshot.RuntimeUnavailable
                ? $"{outcome.RuntimeMessage} Runtime is not running after handling {scope}."
                : $"{outcome.RuntimeMessage} Runtime status after handling {scope} is unavailable: {outcome.Snapshot.RuntimeMessage}";

        return new LidGuardSessionCommandToolResponse
        {
            Summary = summary,
            RequestedCommand = outcome.RequestedCommand,
            RequestedSessionIdentifier = outcome.RequestedSessionIdentifier,
            RequestedProvider = outcome.RequestedProvider,
            RequestedProviderName = outcome.RequestedProviderName,
            Snapshot = outcome.Snapshot
        };
    }
}

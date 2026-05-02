using System.ComponentModel;
using LidGuard.Control;
using LidGuard.Ipc;
using LidGuard.Mcp.Models;
using LidGuard.Sessions;
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
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Call this once when you are starting a brand-new LidGuard Provider MCP session before autonomous work begins. Do not invent or supply a session identifier here. LidGuard generates an 8-character lowercase hexadecimal session identifier from the first block of a new GUID, starts tracking it, and returns that exact value in both requestedSessionIdentifier and sessionIdentifierToReuse. Save that returned identifier and reuse it verbatim with provider_set_soft_lock, provider_clear_soft_lock, and provider_stop_session until the work is truly complete. If you are resuming after a previous soft lock, do not call this again; call provider_clear_soft_lock with the earlier returned session id instead.")]
    public async Task<LidGuardSessionCommandToolResponse> StartSession(
        [Description("Optional working directory for the current task. Pass it when the provider can expose the active project folder.")]
        string workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var sessionIdentifier = CreateGeneratedSessionIdentifier();
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
     Description("Call this only when the work is truly complete and this ongoing Provider MCP session no longer needs LidGuard protection. Pass the exact session identifier that provider_start_session previously returned. Do not generate a new identifier for stop. If you are ending the turn because you need the user's next input, use provider_set_soft_lock instead.")]
    public async Task<LidGuardSessionCommandToolResponse> StopSession(
        [Description("The exact session identifier previously returned by provider_start_session for the ongoing session that is now truly complete.")]
        string sessionIdentifier,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.StopSessionAsync(
            sessionIdentifier,
            AgentProvider.Mcp,
            providerMcpServerConfiguration.ProviderName,
            true,
            "provider_stop_session",
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
     Description("Call this immediately before you end a turn because you need the user's next input and want LidGuard to allow suspend. Pass the exact session identifier previously returned by provider_start_session. A soft-locked session stays tracked, but it stops keeping the machine awake. This tool cannot force the turn to end; after calling it, you must actually end or hand back the conversation yourself and stop autonomous work in that turn.")]
    public async Task<LidGuardSessionCommandToolResponse> SetSoftLock(
        [Description("The exact session identifier previously returned by provider_start_session for the session that should stay tracked but become suspend-eligible.")]
        string sessionIdentifier,
        [Description("Why autonomous work is blocked on user input, such as waiting_for_user_input, waiting_for_clarification, waiting_for_approval, waiting_for_credentials, or waiting_for_manual_step.")]
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
     Description("Call this when a previously soft-locked Provider MCP session can resume autonomous work, typically right after the user has replied and immediately before you continue work. Pass the exact same session identifier that provider_start_session returned earlier. When you are resuming an existing soft-locked session, prefer this tool instead of starting a brand-new session.")]
    public async Task<LidGuardSessionCommandToolResponse> ClearSoftLock(
        [Description("The exact session identifier previously returned by provider_start_session for the session whose earlier soft lock should be cleared.")]
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
        var reuseSummary = outcome.RequestedCommand == LidGuardPipeCommands.Start
            ? $" Reuse session id '{outcome.RequestedSessionIdentifier}' verbatim with provider_set_soft_lock, provider_clear_soft_lock, and provider_stop_session until the work is truly complete."
            : string.Empty;
        var summary = outcome.Snapshot.RuntimeReachable
            ? $"{outcome.RuntimeMessage} Runtime now tracks {outcome.Snapshot.ActiveSessionCount} active session(s) after handling {scope}.{reuseSummary}"
            : outcome.Snapshot.RuntimeUnavailable
                ? $"{outcome.RuntimeMessage} Runtime is not running after handling {scope}.{reuseSummary}"
                : $"{outcome.RuntimeMessage} Runtime status after handling {scope} is unavailable: {outcome.Snapshot.RuntimeMessage}.{reuseSummary}";

        return new LidGuardSessionCommandToolResponse
        {
            Summary = summary,
            RequestedCommand = outcome.RequestedCommand,
            RequestedSessionIdentifier = outcome.RequestedSessionIdentifier,
            SessionIdentifierToReuse = outcome.RequestedSessionIdentifier,
            RequestedProvider = outcome.RequestedProvider,
            RequestedProviderName = outcome.RequestedProviderName,
            Snapshot = outcome.Snapshot
        };
    }

    private static string CreateGeneratedSessionIdentifier() => Guid.NewGuid().ToString("N")[..8];
}

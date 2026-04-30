namespace LidGuard.Ipc;

internal static class LidGuardPipeCommands
{
    public const string CleanupOrphans = "cleanup-orphans";
    public const string ClaudeHook = "claude-hook";
    public const string ClaudeHooks = "claude-hooks";
    public const string CopilotHook = "copilot-hook";
    public const string CopilotHooks = "copilot-hooks";
    public const string CodexHook = "codex-hook";
    public const string CodexHooks = "codex-hooks";
    public const string CurrentMonitorCount = "current-monitor-count";
    public const string CurrentTemperature = "current-temperature";
    public const string HookEvents = "hook-events";
    public const string HookInstall = "hook-install";
    public const string HookRemove = "hook-remove";
    public const string HookStatus = "hook-status";
    public const string MarkSessionActive = "mark-session-active";
    public const string MarkSessionSoftLocked = "mark-session-softlocked";
    public const string McpInstall = "mcp-install";
    public const string McpRemove = "mcp-remove";
    public const string McpStatus = "mcp-status";
    public const string PreviewSystemSound = "preview-system-sound";
    public const string ProviderMcpInstall = "provider-mcp-install";
    public const string ProviderMcpRemove = "provider-mcp-remove";
    public const string ProviderMcpStatus = "provider-mcp-status";
    public const string RemovePreSuspendWebhook = "remove-pre-suspend-webhook";
    public const string RemoveSession = "remove-session";
    public const string RunServer = "run-server";
    public const string Settings = "settings";
    public const string Start = "start";
    public const string Status = "status";
    public const string Stop = "stop";
}


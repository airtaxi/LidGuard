namespace LidGuard.Commands.Help;

internal static class LidGuardHelpCommandCatalog
{
    internal static IReadOnlyList<LidGuardHelpCommandEntry> CreateCommandEntries(LidGuardHelpDocumentContext documentContext)
    {
        return
        [
            StartHelpContent.Create(documentContext),
            StopHelpContent.Create(documentContext),
            RemoveSessionHelpContent.Create(documentContext),
            StatusHelpContent.Create(documentContext),
            CleanupOrphansHelpContent.Create(documentContext),
            HelpHelpContent.Create(documentContext),
            SettingsHelpContent.Create(documentContext),
            RemovePreSuspendWebhookHelpContent.Create(documentContext),
            RemovePostSessionEndWebhookHelpContent.Create(documentContext),
            PreviewSystemSoundHelpContent.Create(documentContext),
            PreviewCurrentSoundHelpContent.Create(documentContext),
            CurrentLidStateHelpContent.Create(documentContext),
            CurrentMonitorCountHelpContent.Create(documentContext),
            CurrentTemperatureHelpContent.Create(documentContext),
            SuspendHistoryHelpContent.Create(documentContext),
#if LIDGUARD_LINUX
            LinuxPermissionHelpContent.Create(documentContext),
#endif
            HookStatusHelpContent.Create(documentContext),
            HookInstallHelpContent.Create(documentContext),
            HookRemoveHelpContent.Create(documentContext),
            HookEventsHelpContent.Create(documentContext),
            CodexHooksHelpContent.Create(documentContext),
            ClaudeHooksHelpContent.Create(documentContext),
            CopilotHooksHelpContent.Create(documentContext),
            McpStatusHelpContent.Create(documentContext),
            McpInstallHelpContent.Create(documentContext),
            McpRemoveHelpContent.Create(documentContext),
            ProviderMcpStatusHelpContent.Create(documentContext),
            ProviderMcpInstallHelpContent.Create(documentContext),
            ProviderMcpRemoveHelpContent.Create(documentContext),
            McpServerHelpContent.Create(documentContext),
            ProviderMcpServerHelpContent.Create(documentContext),
            CodexHookHelpContent.Create(documentContext),
            ClaudeHookHelpContent.Create(documentContext),
            CopilotHookHelpContent.Create(documentContext)
        ];
    }
}

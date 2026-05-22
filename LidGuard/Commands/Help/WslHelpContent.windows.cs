using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class WslHookStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateHookManagementEntry(context, LidGuardPipeCommands.WslHookStatus, "wsl-hook-status", LocalizationService.GetString("Help_WslHookStatus_Description"));
}

internal static class WslHookInstallHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateHookManagementEntry(context, LidGuardPipeCommands.WslHookInstall, "wsl-hook-install", LocalizationService.GetString("Help_WslHookInstall_Description"));
}

internal static class WslHookRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateHookManagementEntry(context, LidGuardPipeCommands.WslHookRemove, "wsl-hook-remove", LocalizationService.GetString("Help_WslHookRemove_Description"));
}

internal static class WslCodexHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateHookSnippetEntry(context, LidGuardPipeCommands.WslCodexHooks, "wsl-codex-hooks", "[config-toml|toml|hooks-json|json]", LocalizationService.GetString("Help_WslCodexHooks_Description"), LocalizationService.GetString("Help_CodexHooks_FormatOption"));
}

internal static class WslClaudeHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateHookSnippetEntry(context, LidGuardPipeCommands.WslClaudeHooks, "wsl-claude-hooks", "[settings-json|json|hooks-json]", LocalizationService.GetString("Help_WslClaudeHooks_Description"), LocalizationService.GetString("Help_ClaudeHooks_FormatOption"));
}

internal static class WslCopilotHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateHookSnippetEntry(context, LidGuardPipeCommands.WslCopilotHooks, "wsl-copilot-hooks", "[config-json|json|hooks-json]", LocalizationService.GetString("Help_WslCopilotHooks_Description"), LocalizationService.GetString("Help_CopilotHooks_FormatOption"));
}

internal static class WslMcpStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateMcpManagementEntry(context, LidGuardPipeCommands.WslMcpStatus, "wsl-mcp-status", LocalizationService.GetString("Help_WslMcpStatus_Description"));
}

internal static class WslMcpInstallHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateMcpManagementEntry(context, LidGuardPipeCommands.WslMcpInstall, "wsl-mcp-install", LocalizationService.GetString("Help_WslMcpInstall_Description"));
}

internal static class WslMcpRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateMcpManagementEntry(context, LidGuardPipeCommands.WslMcpRemove, "wsl-mcp-remove", LocalizationService.GetString("Help_WslMcpRemove_Description"));
}

internal static class WslProviderMcpStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateProviderMcpEntry(context, LidGuardPipeCommands.WslProviderMcpStatus, "wsl-provider-mcp-status", "--config <json-path> [--server-name <name>]", LocalizationService.GetString("Help_WslProviderMcpStatus_Description"), false);
}

internal static class WslProviderMcpInstallHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateProviderMcpEntry(context, LidGuardPipeCommands.WslProviderMcpInstall, "wsl-provider-mcp-install", "--config <json-path> --provider-name <name> [--server-name <name>]", LocalizationService.GetString("Help_WslProviderMcpInstall_Description"), true);
}

internal static class WslProviderMcpRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
        => WslHelpContentFactory.CreateProviderMcpEntry(context, LidGuardPipeCommands.WslProviderMcpRemove, "wsl-provider-mcp-remove", "--config <json-path> [--server-name <name>]", LocalizationService.GetString("Help_WslProviderMcpRemove_Description"), false);
}

internal static class WslHelpContentFactory
{
    internal static LidGuardHelpCommandEntry CreateHookManagementEntry(LidGuardHelpDocumentContext context, string commandName, string synopsisCommandName, string description)
    {
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(commandName, [], LidGuardHelpSectionTitles.HookIntegration, $"{context.CommandDisplayName} {synopsisCommandName} [--provider codex|claude|copilot|all] [--config <path>] [--distro <name>]", description, [new LidGuardHelpOption("--provider <provider>", LocalizationService.GetString("Help_ManagedProvider_ProviderOption")), new LidGuardHelpOption("--config <path>", LocalizationService.GetString("Help_ManagedProvider_ConfigOption")), CreateDistroOption()], CreateWslNotes());
    }

    internal static LidGuardHelpCommandEntry CreateHookSnippetEntry(LidGuardHelpDocumentContext context, string commandName, string synopsisCommandName, string formatSynopsis, string description, string formatDescription)
    {
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(commandName, [], LidGuardHelpSectionTitles.HookIntegration, $"{context.CommandDisplayName} {synopsisCommandName} {formatSynopsis} [--distro <name>]", description, [new LidGuardHelpOption("<format>", formatDescription), CreateDistroOption()], CreateWslNotes());
    }

    internal static LidGuardHelpCommandEntry CreateMcpManagementEntry(LidGuardHelpDocumentContext context, string commandName, string synopsisCommandName, string description)
    {
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(commandName, CreateProviderMcpAliases(synopsisCommandName), LidGuardHelpSectionTitles.McpIntegration, $"{context.CommandDisplayName} {synopsisCommandName} [codex|claude|copilot|all] [--distro <name>]", description, [new LidGuardHelpOption("<provider>", LocalizationService.GetString("Help_Mcp_ProviderArgument")), CreateDistroOption()], CreateWslNotes());
    }

    internal static LidGuardHelpCommandEntry CreateProviderMcpEntry(LidGuardHelpDocumentContext context, string commandName, string synopsisCommandName, string argumentSynopsis, string description, bool includeProviderName)
    {
        var options = new List<LidGuardHelpOption>
        {
            new("--config <json-path>", LocalizationService.GetString("Help_ProviderMcpInstall_ConfigOption")),
            new("--server-name <name>", LocalizationService.GetString("Help_ProviderMcp_ServerNameOption")),
            CreateDistroOption()
        };
        if (includeProviderName) options.Insert(1, new LidGuardHelpOption("--provider-name <name>", LocalizationService.GetString("Help_ProviderMcpInstall_ProviderNameOption")));

        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(commandName, [], LidGuardHelpSectionTitles.McpIntegration, $"{context.CommandDisplayName} {synopsisCommandName} {argumentSynopsis} [--distro <name>]", description, options, CreateWslNotes());
    }

    private static LidGuardHelpOption CreateDistroOption()
        => new("--distro <name>", LocalizationService.GetString("Help_Wsl_DistroOption"));

    private static IReadOnlyList<string> CreateWslNotes()
        =>
        [
            LocalizationService.GetString("Help_Wsl_WindowsOnlyNote"),
            LocalizationService.GetString("Help_Wsl_CommandNote")
        ];

    private static IReadOnlyList<string> CreateProviderMcpAliases(string synopsisCommandName)
    {
        var operationName = synopsisCommandName switch
        {
            "wsl-mcp-status" => "status",
            "wsl-mcp-install" => "install",
            "wsl-mcp-remove" => "remove",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(operationName)) return [];

        return
        [
            $"wsl-codex-mcp-{operationName}",
            $"wsl-claude-mcp-{operationName}",
            $"wsl-copilot-mcp-{operationName}"
        ];
    }
}

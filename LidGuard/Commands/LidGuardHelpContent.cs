using LidGuard.Ipc;
using LidGuard.Mcp;

namespace LidGuard.Commands;

internal static class LidGuardHelpContent
{
    private const string UsageSectionTitle = "Usage";
    private const string SessionControlSectionTitle = "Session Control";
    private const string SettingsAndSuspendSectionTitle = "Settings & Suspend";
    private const string DiagnosticsSectionTitle = "Diagnostics";
    private const string HookIntegrationSectionTitle = "Hook Integration";
    private const string McpIntegrationSectionTitle = "MCP Integration";
    private const string ManagedAndInternalCommandsSectionTitle = "Managed / Internal Commands";
    private const string PathsAndNotesSectionTitle = "Paths & Notes";

    public static LidGuardHelpDocument CreateDocument(
        string commandDisplayName,
        string settingsFilePath,
        string sessionLogFilePath,
        string suspendHistoryLogFilePath,
        string supportedPostStopSuspendSystemSounds)
    {
        var documentContext = new LidGuardHelpDocumentContext(
            commandDisplayName,
            settingsFilePath,
            sessionLogFilePath,
            suspendHistoryLogFilePath,
            supportedPostStopSuspendSystemSounds);

        return new LidGuardHelpDocument(
            documentContext,
            CreateSectionEntries(documentContext),
            CreateCommandEntries(documentContext));
    }

    public static IReadOnlyList<LidGuardHelpSection> CreateSections(
        string commandDisplayName,
        string settingsFilePath,
        string sessionLogFilePath,
        string suspendHistoryLogFilePath,
        string supportedPostStopSuspendSystemSounds)
        => CreateAllSections(CreateDocument(
            commandDisplayName,
            settingsFilePath,
            sessionLogFilePath,
            suspendHistoryLogFilePath,
            supportedPostStopSuspendSystemSounds));

    public static bool TryFindCommand(
        LidGuardHelpDocument document,
        string commandName,
        out LidGuardHelpCommandEntry commandEntry)
    {
        commandEntry = default;
        var normalizedCommandName = commandName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommandName)) return false;

        foreach (var candidateCommandEntry in document.CommandEntries)
        {
            if (candidateCommandEntry.CanonicalName.Equals(normalizedCommandName, StringComparison.OrdinalIgnoreCase))
            {
                commandEntry = candidateCommandEntry;
                return true;
            }

            foreach (var alias in candidateCommandEntry.Aliases)
            {
                if (!alias.Equals(normalizedCommandName, StringComparison.OrdinalIgnoreCase)) continue;

                commandEntry = candidateCommandEntry;
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<LidGuardHelpSection> CreateAllSections(LidGuardHelpDocument document)
    {
        var helpSections = new List<LidGuardHelpSection>();
        foreach (var sectionEntry in document.SectionEntries)
        {
            var helpCommands = CreateHelpCommandsForSection(document, sectionEntry.Title);
            helpSections.Add(new LidGuardHelpSection(sectionEntry.Title, sectionEntry.Details, helpCommands));
        }

        return helpSections;
    }

    public static IReadOnlyList<LidGuardHelpSection> CreateSummarySections(LidGuardHelpDocument document)
    {
        var helpSections = new List<LidGuardHelpSection>
        {
            new(UsageSectionTitle, CreateSummaryUsageDetails(document.Context.CommandDisplayName), [])
        };

        foreach (var sectionEntry in document.SectionEntries)
        {
            if (sectionEntry.Title.Equals(UsageSectionTitle, StringComparison.Ordinal)) continue;
            if (sectionEntry.Title.Equals(PathsAndNotesSectionTitle, StringComparison.Ordinal)) continue;

            var helpCommands = CreateSummaryCommandsForSection(document, sectionEntry.Title);
            if (helpCommands.Count == 0) continue;

            helpSections.Add(new LidGuardHelpSection(sectionEntry.Title, sectionEntry.Details, helpCommands));
        }

        return helpSections;
    }

    public static IReadOnlyList<LidGuardHelpSection> CreateCommandSections(
        LidGuardHelpDocument document,
        LidGuardHelpCommandEntry commandEntry)
    {
        return
        [
            new LidGuardHelpSection(UsageSectionTitle, CreateCommandUsageDetails(commandEntry), []),
            new LidGuardHelpSection(
                commandEntry.SectionTitle,
                CreateSectionDetails(document, commandEntry.SectionTitle),
                commandEntry.HelpCommands)
        ];
    }

    private static IReadOnlyList<LidGuardHelpSectionEntry> CreateSectionEntries(LidGuardHelpDocumentContext documentContext)
    {
        return
        [
            new LidGuardHelpSectionEntry(UsageSectionTitle, CreateUsageDetails(documentContext.CommandDisplayName)),
            new LidGuardHelpSectionEntry(SessionControlSectionTitle, []),
            new LidGuardHelpSectionEntry(SettingsAndSuspendSectionTitle, []),
            new LidGuardHelpSectionEntry(DiagnosticsSectionTitle, []),
            new LidGuardHelpSectionEntry(HookIntegrationSectionTitle, []),
            new LidGuardHelpSectionEntry(McpIntegrationSectionTitle, []),
            new LidGuardHelpSectionEntry(
                ManagedAndInternalCommandsSectionTitle,
                [
                    "These commands are intended for provider-managed integrations and stdio hosts rather than direct everyday CLI use."
                ]),
            new LidGuardHelpSectionEntry(
                PathsAndNotesSectionTitle,
                CreatePathsAndNotesDetails(
                    documentContext.SettingsFilePath,
                    documentContext.SessionLogFilePath,
                    documentContext.SuspendHistoryLogFilePath))
        ];
    }

    private static IReadOnlyList<LidGuardHelpCommandEntry> CreateCommandEntries(LidGuardHelpDocumentContext documentContext)
    {
        var commandDisplayName = documentContext.CommandDisplayName;
        var supportedPostStopSuspendSystemSounds = documentContext.SupportedPostStopSuspendSystemSounds;

        return
        [
            CreateSingleCommandEntry(
                LidGuardPipeCommands.Start,
                [],
                SessionControlSectionTitle,
                $"{commandDisplayName} start --provider codex|claude|copilot|custom|mcp [--session <id>] [--provider-name <name>] [--parent-pid <pid>] [--working-directory <path>]",
                "Start or refresh a tracked session and load persisted default settings into the runtime request.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Required. Allowed values: codex, claude, copilot, custom, or mcp."),
                    new LidGuardHelpOption("--session <id>", "Optional. Session identifier to track. When omitted, LidGuard derives one from the provider display name and normalized working directory."),
                    new LidGuardHelpOption("--provider-name <name>", "Required when --provider mcp is used. Distinguishes one MCP-backed provider from another."),
                    new LidGuardHelpOption("--parent-pid <pid>", "Optional non-negative watched process identifier used by the runtime watchdog."),
                    new LidGuardHelpOption("--working-directory <path>", "Optional working directory used for fallback session identity and process resolution. Defaults to the current directory.")
                ],
                [
                    "If no runtime is listening, start launches the detached runtime server automatically."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.Stop,
                [],
                SessionControlSectionTitle,
                $"{commandDisplayName} stop --provider codex|claude|copilot|custom|mcp [--session <id>] [--provider-name <name>] [--parent-pid <pid>] [--working-directory <path>]",
                "Stop a tracked session by matching the same provider and session identity used when the session started.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Required. Allowed values: codex, claude, copilot, custom, or mcp."),
                    new LidGuardHelpOption("--session <id>", "Optional. When omitted, LidGuard uses the same fallback session identifier strategy as start."),
                    new LidGuardHelpOption("--provider-name <name>", "Required when --provider mcp is used."),
                    new LidGuardHelpOption("--parent-pid <pid>", "Optional non-negative watched process identifier."),
                    new LidGuardHelpOption("--working-directory <path>", "Optional working directory used for fallback session identity. Defaults to the current directory.")
                ],
                []),
            new LidGuardHelpCommandEntry(
                LidGuardPipeCommands.RemoveSession,
                [],
                SessionControlSectionTitle,
                "Remove active sessions currently tracked by the runtime.",
                [
                    new LidGuardHelpCommand(
                        $"{commandDisplayName} remove-session --all",
                        "Remove every active session currently tracked by the runtime.",
                        [],
                        [
                            "--all cannot be combined with --session, --provider, or --provider-name."
                        ]),
                    new LidGuardHelpCommand(
                        $"{commandDisplayName} remove-session --session <id> [--provider codex|claude|copilot|custom|mcp|unknown] [--provider-name <name>]",
                        "Remove active sessions by session identifier without waiting for provider stop hooks.",
                        [
                            new LidGuardHelpOption("--session <id>", "Required. Session identifier to remove."),
                            new LidGuardHelpOption("--provider <provider>", "Optional. Narrows removal to one provider. Allowed values: codex, claude, copilot, custom, mcp, or unknown."),
                            new LidGuardHelpOption("--provider-name <name>", "Optional. Narrows removal to one MCP provider name when --provider mcp is used.")
                        ],
                        [
                            "When --provider is omitted, LidGuard removes every active session whose session identifier matches.",
                            "When --provider mcp is used without --provider-name, LidGuard removes every MCP-backed session with the same session identifier."
                        ])
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.Status,
                [],
                SessionControlSectionTitle,
                $"{commandDisplayName} status",
                "Show runtime state, active sessions, and effective stored settings.",
                [],
                [
                    "If the runtime is not running, status still prints the stored settings file contents."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CleanupOrphans,
                [],
                SessionControlSectionTitle,
                $"{commandDisplayName} cleanup-orphans",
                "Remove sessions whose watched processes have already exited.",
                [],
                [
                    "If the runtime is not running, cleanup-orphans reports that nothing needs cleanup."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.Help,
                [],
                SessionControlSectionTitle,
                $"{commandDisplayName} help [command]",
                "Show the categorized command overview or focused detailed help for one known command or alias.",
                [],
                [
                    "The <command> --help form uses the same command metadata."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.Settings,
                [],
                SettingsAndSuspendSectionTitle,
                $"{commandDisplayName} settings [--reset <bool>] [--change-lid-action <bool>] [--prevent-system-sleep <bool>] [--prevent-away-mode-sleep <bool>] [--prevent-display-sleep <bool>] [--watch-parent-process <bool>] [--emergency-hibernation-on-high-temperature <bool>] [--emergency-hibernation-temperature-mode low|average|high] [--emergency-hibernation-temperature-celsius <number>] [--suspend-mode sleep|hibernate] [--post-stop-suspend-delay-seconds <number>] [--post-stop-suspend-sound off|<system-sound>|<wav-path>] [--post-stop-suspend-sound-volume-override-percent off|<1-100>] [--suspend-history-count off|<count>] [--pre-suspend-webhook-url <http-or-https-url>] [--closed-lid-permission-request-decision deny|allow] [--power-request-reason <text>]",
                "Show and update the persisted default settings used by start and hook-driven runtime requests.",
                [
                    new LidGuardHelpOption("--reset <bool>", "When true, start from headless runtime defaults before applying the other supplied options."),
                    new LidGuardHelpOption("--change-lid-action <bool>", "Toggle the temporary active power plan lid close action override."),
                    new LidGuardHelpOption("--prevent-system-sleep <bool>", "Toggle PowerRequestSystemRequired handling."),
                    new LidGuardHelpOption("--prevent-away-mode-sleep <bool>", "Toggle PowerRequestAwayModeRequired handling."),
                    new LidGuardHelpOption("--prevent-display-sleep <bool>", "Toggle PowerRequestDisplayRequired handling."),
                    new LidGuardHelpOption("--watch-parent-process <bool>", "Toggle the process exit watchdog for tracked sessions."),
                    new LidGuardHelpOption("--emergency-hibernation-on-high-temperature <bool>", "Toggle Emergency Hibernation when the guarded system temperature reaches the configured threshold while the lid is closed."),
                    new LidGuardHelpOption("--emergency-hibernation-temperature-mode low|average|high", "Choose whether LidGuard compares the low, average, or high recognized thermal-zone temperature."),
                    new LidGuardHelpOption("--emergency-hibernation-temperature-celsius <number>", "Set the Emergency Hibernation threshold in Celsius. Allowed range: 70 through 110."),
                    new LidGuardHelpOption("--suspend-mode sleep|hibernate", "Choose the suspend action requested after the last active session stops or becomes soft-locked."),
                    new LidGuardHelpOption("--post-stop-suspend-delay-seconds <number>", "Set the suspend delay in seconds. Use 0 for immediate suspend."),
                    new LidGuardHelpOption("--post-stop-suspend-sound off|<system-sound>|<wav-path>", $"Disable the pre-suspend sound, use one supported SystemSound name ({supportedPostStopSuspendSystemSounds}), or supply an existing playable .wav path."),
                    new LidGuardHelpOption("--post-stop-suspend-sound-volume-override-percent off|<1-100>", "Disable the volume override or temporarily set the default output device master volume while the post-stop suspend sound plays, then restore the previous volume and mute state."),
                    new LidGuardHelpOption("--suspend-history-count off|<count>", "Disable suspend history recording or retain the most recent suspend request entries. Minimum enabled value is 1."),
                    new LidGuardHelpOption("--pre-suspend-webhook-url <http-or-https-url>", "Set the absolute HTTP or HTTPS webhook called before suspend."),
                    new LidGuardHelpOption("--closed-lid-permission-request-decision deny|allow", "Choose how closed-lid PermissionRequest hooks respond when the runtime reports the lid is closed."),
                    new LidGuardHelpOption("--power-request-reason <text>", "Set the power request reason text shown to Windows.")
                ],
                [
                    "Running settings with no options enters interactive edit mode.",
                    "Emergency Hibernation temperature mode defaults to Average.",
                    "Emergency Hibernation temperature defaults to 93 Celsius and is clamped to 70 through 110 at runtime.",
                    "Post-stop suspend delay defaults to 10 seconds.",
                    "Post-stop suspend sound volume override defaults to off; pass off to disable it.",
                    "Suspend history recording defaults to on and keeps the latest 10 entries.",
                    "Use remove-pre-suspend-webhook to clear a configured webhook URL instead of passing off or an empty value."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.RemovePreSuspendWebhook,
                [],
                SettingsAndSuspendSectionTitle,
                $"{commandDisplayName} remove-pre-suspend-webhook",
                "Clear the persisted pre-suspend webhook URL.",
                [],
                [
                    "This command does not accept any options."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.PreviewSystemSound,
                [],
                SettingsAndSuspendSectionTitle,
                $"{commandDisplayName} preview-system-sound --name Asterisk|Beep|Exclamation|Hand|Question",
                "Play one supported SystemSound name immediately using the saved post-stop suspend sound volume override setting.",
                [
                    new LidGuardHelpOption("--name <sound>", "Required. Allowed values: Asterisk, Beep, Exclamation, Hand, or Question.")
                ],
                [
                    "This command waits until playback finishes."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.PreviewCurrentSound,
                [],
                SettingsAndSuspendSectionTitle,
                $"{commandDisplayName} preview-current-sound",
                "Play the saved post-stop suspend sound immediately using the saved volume override setting.",
                [],
                [
                    "If no post-stop suspend sound is configured, this command prints settings guidance instead of failing.",
                    "This command waits until playback finishes."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CurrentLidState,
                [],
                DiagnosticsSectionTitle,
                $"{commandDisplayName} {LidGuardPipeCommands.CurrentLidState}",
                "Report the current lid switch state using the same Windows lid-state source LidGuard uses for closed-lid policy decisions.",
                [],
                [
                    "This reports Open, Closed, or Unknown based on the current `GUID_LIDSWITCH_STATE_CHANGE` value."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CurrentMonitorCount,
                [],
                DiagnosticsSectionTitle,
                $"{commandDisplayName} {LidGuardPipeCommands.CurrentMonitorCount}",
                "Report the current visible display monitor count using the same base Windows monitor visibility check LidGuard uses for closed-lid policy decisions.",
                [],
                [
                    "This starts from `GetSystemMetrics(SM_CMONITORS)` and excludes inactive monitor connections reported by Windows WMI. Internal laptop panel connections are only excluded by the final suspend eligibility check."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CurrentTemperature,
                [],
                DiagnosticsSectionTitle,
                $"{commandDisplayName} {LidGuardPipeCommands.CurrentTemperature} [--temperature-mode default|low|average|high]",
                "Report the current recognized system thermal-zone temperature in Celsius using the selected aggregation mode.",
                [
                    new LidGuardHelpOption("--temperature-mode default|low|average|high", "Optional. Use the saved LidGuard setting with default, or override it with low, average, or high for this command only.")
                ],
                [
                    "If Windows does not currently expose thermal-zone temperature data, the command reports that the value is unavailable.",
                    "When the settings file does not exist yet, default uses LidGuard's headless runtime default mode: Average."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.SuspendHistory,
                [],
                DiagnosticsSectionTitle,
                $"{commandDisplayName} {LidGuardPipeCommands.SuspendHistory} [--count <number>]",
                "Print recent suspend request history from the local suspend history log.",
                [
                    new LidGuardHelpOption("--count <number>", "Optional positive entry count to display. Defaults to the saved suspend-history-count value, or 10 when recording is off.")
                ],
                [
                    "The saved suspend-history-count setting controls how many entries are retained. The --count option only limits how many retained entries are displayed."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.HookStatus,
                [],
                HookIntegrationSectionTitle,
                $"{commandDisplayName} hook-status [--provider codex|claude|copilot|all] [--config <path>]",
                "Inspect the managed hook configuration for one provider or every detected provider.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."),
                    new LidGuardHelpOption("--config <path>", "Optional provider-specific configuration file override.")
                ],
                [
                    "Do not combine --config with --provider all because each provider uses a different configuration file.",
                    "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.HookInstall,
                [],
                HookIntegrationSectionTitle,
                $"{commandDisplayName} hook-install [--provider codex|claude|copilot|all] [--config <path>]",
                "Install the managed provider hook entries into the selected configuration file.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."),
                    new LidGuardHelpOption("--config <path>", "Optional provider-specific configuration file override.")
                ],
                [
                    "Do not combine --config with --provider all because each provider uses a different configuration file.",
                    "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.HookRemove,
                ["hook-uninstall"],
                HookIntegrationSectionTitle,
                $"{commandDisplayName} hook-remove [--provider codex|claude|copilot|all] [--config <path>]",
                "Remove the managed provider hook entries from the selected configuration file.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."),
                    new LidGuardHelpOption("--config <path>", "Optional provider-specific configuration file override.")
                ],
                [
                    "Do not combine --config with --provider all because each provider uses a different configuration file.",
                    "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.HookEvents,
                [],
                HookIntegrationSectionTitle,
                $"{commandDisplayName} hook-events [--provider codex|claude|copilot|all] [--count <number>]",
                "Print recent hook event log lines for the selected provider or providers.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."),
                    new LidGuardHelpOption("--count <number>", "Optional positive line count. Defaults to 50.")
                ],
                [
                    "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CodexHooks,
                [],
                HookIntegrationSectionTitle,
                $"{commandDisplayName} codex-hooks [--format config-toml|hooks-json]",
                "Print a managed Codex hook configuration snippet.",
                [
                    new LidGuardHelpOption("--format <format>", "Optional. Defaults to config-toml. Also accepts toml or hooks-json.")
                ],
                []),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.ClaudeHooks,
                [],
                HookIntegrationSectionTitle,
                $"{commandDisplayName} claude-hooks [--format settings-json|hooks-json]",
                "Print a managed Claude Code hook configuration snippet.",
                [
                    new LidGuardHelpOption("--format <format>", "Optional. Defaults to settings-json. Also accepts json or hooks-json.")
                ],
                []),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CopilotHooks,
                [],
                HookIntegrationSectionTitle,
                $"{commandDisplayName} copilot-hooks [--format config-json|hooks-json]",
                "Print a managed GitHub Copilot CLI hook configuration snippet.",
                [
                    new LidGuardHelpOption("--format <format>", "Optional. Defaults to config-json. Also accepts json or hooks-json.")
                ],
                []),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.McpStatus,
                [],
                McpIntegrationSectionTitle,
                $"{commandDisplayName} mcp-status [--provider codex|claude|copilot|all]",
                "Inspect the managed user/global LidGuard MCP server registration for one provider or every detected provider.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
                ],
                [
                    "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.McpInstall,
                [],
                McpIntegrationSectionTitle,
                $"{commandDisplayName} mcp-install [--provider codex|claude|copilot|all]",
                "Register or refresh the managed stdio MCP server named lidguard with the selected provider CLI.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
                ],
                [
                    "If an existing managed LidGuard MCP server is found, mcp-install removes it first and then installs the current command.",
                    "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.McpRemove,
                ["mcp-uninstall"],
                McpIntegrationSectionTitle,
                $"{commandDisplayName} mcp-remove [--provider codex|claude|copilot|all]",
                "Remove the managed stdio MCP server named lidguard from the selected provider CLI.",
                [
                    new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
                ],
                [
                    "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.ProviderMcpStatus,
                [],
                McpIntegrationSectionTitle,
                $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpStatus} --config <json-path> [--server-name <name>]",
                "Inspect a caller-supplied JSON configuration file for a managed provider MCP server entry.",
                [
                    new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to inspect."),
                    new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
                ],
                []),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.ProviderMcpInstall,
                [],
                McpIntegrationSectionTitle,
                $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpInstall} --config <json-path> --provider-name <name> [--server-name <name>]",
                "Write or update a managed provider MCP stdio server entry in a caller-supplied JSON configuration file.",
                [
                    new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to create or update."),
                    new LidGuardHelpOption("--provider-name <name>", "Required provider name passed through to provider-mcp-server."),
                    new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
                ],
                [
                    "This path edits the supplied JSON file directly and does not call provider-specific mcp add/remove commands."
                ]),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.ProviderMcpRemove,
                ["provider-mcp-uninstall"],
                McpIntegrationSectionTitle,
                $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpRemove} --config <json-path> [--server-name <name>]",
                "Remove a managed provider MCP server entry from a caller-supplied JSON configuration file.",
                [
                    new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to update."),
                    new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
                ],
                []),
            CreateSingleCommandEntry(
                LidGuardMcpServerCommand.CommandName,
                [],
                ManagedAndInternalCommandsSectionTitle,
                $"{commandDisplayName} {LidGuardMcpServerCommand.CommandName}",
                "Host the regular LidGuard stdio MCP server that exposes settings and session management tools.",
                [],
                []),
            CreateSingleCommandEntry(
                ProviderMcpServerCommand.CommandName,
                [],
                ManagedAndInternalCommandsSectionTitle,
                $"{commandDisplayName} {ProviderMcpServerCommand.CommandName} --provider-name <name>",
                "Host the dedicated provider MCP stdio server for a single caller-supplied provider name.",
                [
                    new LidGuardHelpOption("--provider-name <name>", "Required provider name exposed to the provider MCP tools.")
                ],
                []),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CodexHook,
                [],
                ManagedAndInternalCommandsSectionTitle,
                $"{commandDisplayName} codex-hook",
                "Read Codex hook JSON from standard input and forward start, stop, or closed-lid permission decisions to the runtime.",
                [],
                []),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.ClaudeHook,
                [],
                ManagedAndInternalCommandsSectionTitle,
                $"{commandDisplayName} claude-hook",
                "Read Claude Code hook JSON from standard input and forward start, stop, activity, soft-lock, elicitation, or permission decisions to the runtime.",
                [],
                []),
            CreateSingleCommandEntry(
                LidGuardPipeCommands.CopilotHook,
                [],
                ManagedAndInternalCommandsSectionTitle,
                $"{commandDisplayName} copilot-hook --event <event-name>",
                "Read GitHub Copilot CLI hook JSON from standard input for one configured event name.",
                [
                    new LidGuardHelpOption("--event <event-name>", "Required. Typical values include sessionStart, sessionEnd, userPromptSubmitted, preToolUse, postToolUse, permissionRequest, agentStop, errorOccurred, and notification.")
                ],
                [])
        ];
    }

    private static LidGuardHelpCommandEntry CreateSingleCommandEntry(
        string canonicalName,
        IReadOnlyList<string> aliases,
        string sectionTitle,
        string synopsis,
        string description,
        IReadOnlyList<LidGuardHelpOption> options,
        IReadOnlyList<string> notes)
        => new(
            canonicalName,
            aliases,
            sectionTitle,
            description,
            [
                new LidGuardHelpCommand(synopsis, description, options, notes)
            ]);

    private static LidGuardHelpCommandEntry CreateSingleCommandEntry(
        string canonicalName,
        IReadOnlyList<string> aliases,
        string sectionTitle,
        string summaryDescription,
        IReadOnlyList<LidGuardHelpCommand> helpCommands)
        => new(canonicalName, aliases, sectionTitle, summaryDescription, helpCommands);

    private static IReadOnlyList<string> CreateUsageDetails(string commandDisplayName)
    {
        return
        [
            $"{commandDisplayName} <command> [options]",
            "Use --name value or --name=value for options.",
            "Boolean options accept true/false, yes/no, on/off, and 1/0.",
            "Quote paths or text values when they contain spaces."
        ];
    }

    private static IReadOnlyList<string> CreateSummaryUsageDetails(string commandDisplayName)
    {
        return
        [
            $"{commandDisplayName} <command> [options]",
            $"{commandDisplayName} help <command>",
            $"{commandDisplayName} <command> --help"
        ];
    }

    private static IReadOnlyList<string> CreateCommandUsageDetails(LidGuardHelpCommandEntry commandEntry)
    {
        var usageDetails = new List<string>();
        foreach (var helpCommand in commandEntry.HelpCommands) usageDetails.Add(helpCommand.Synopsis);
        return usageDetails;
    }

    private static IReadOnlyList<string> CreatePathsAndNotesDetails(
        string settingsFilePath,
        string sessionLogFilePath,
        string suspendHistoryLogFilePath)
    {
        return
        [
            $"Settings file: {settingsFilePath}",
            $"Session log: {sessionLogFilePath}",
            $"Suspend history log: {suspendHistoryLogFilePath}",
            "Windows runtime behavior is implemented today. macOS and Linux currently print a support-planned message and exit successfully.",
            "Provider MCP integrations are best-effort only because correct behavior depends on the model calling the LidGuard MCP tools at the right times."
        ];
    }

    private static IReadOnlyList<string> CreateSectionDetails(LidGuardHelpDocument document, string sectionTitle)
    {
        foreach (var sectionEntry in document.SectionEntries)
        {
            if (sectionEntry.Title.Equals(sectionTitle, StringComparison.Ordinal)) return sectionEntry.Details;
        }

        return [];
    }

    private static IReadOnlyList<LidGuardHelpCommand> CreateHelpCommandsForSection(LidGuardHelpDocument document, string sectionTitle)
    {
        var helpCommands = new List<LidGuardHelpCommand>();
        foreach (var commandEntry in document.CommandEntries)
        {
            if (!commandEntry.SectionTitle.Equals(sectionTitle, StringComparison.Ordinal)) continue;
            foreach (var helpCommand in commandEntry.HelpCommands) helpCommands.Add(helpCommand);
        }

        return helpCommands;
    }

    private static IReadOnlyList<LidGuardHelpCommand> CreateSummaryCommandsForSection(LidGuardHelpDocument document, string sectionTitle)
    {
        var helpCommands = new List<LidGuardHelpCommand>();
        foreach (var commandEntry in document.CommandEntries)
        {
            if (!commandEntry.SectionTitle.Equals(sectionTitle, StringComparison.Ordinal)) continue;

            helpCommands.Add(new LidGuardHelpCommand(
                CreateSummaryCommandLabel(commandEntry),
                commandEntry.SummaryDescription,
                [],
                []));
        }

        return helpCommands;
    }

    private static string CreateSummaryCommandLabel(LidGuardHelpCommandEntry commandEntry)
    {
        if (commandEntry.Aliases.Count == 0) return commandEntry.CanonicalName;

        return $"{commandEntry.CanonicalName} (alias: {string.Join(", ", commandEntry.Aliases)})";
    }
}

internal sealed class LidGuardHelpDocument(
    LidGuardHelpDocumentContext context,
    IReadOnlyList<LidGuardHelpSectionEntry> sectionEntries,
    IReadOnlyList<LidGuardHelpCommandEntry> commandEntries)
{
    public LidGuardHelpDocumentContext Context { get; } = context;

    public IReadOnlyList<LidGuardHelpSectionEntry> SectionEntries { get; } = sectionEntries;

    public IReadOnlyList<LidGuardHelpCommandEntry> CommandEntries { get; } = commandEntries;
}

internal readonly record struct LidGuardHelpDocumentContext(
    string CommandDisplayName,
    string SettingsFilePath,
    string SessionLogFilePath,
    string SuspendHistoryLogFilePath,
    string SupportedPostStopSuspendSystemSounds);

internal readonly record struct LidGuardHelpCommandEntry(
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    string SectionTitle,
    string SummaryDescription,
    IReadOnlyList<LidGuardHelpCommand> HelpCommands);

internal readonly record struct LidGuardHelpSectionEntry(
    string Title,
    IReadOnlyList<string> Details);

internal readonly record struct LidGuardHelpSection(
    string Title,
    IReadOnlyList<string> Details,
    IReadOnlyList<LidGuardHelpCommand> Commands);

internal readonly record struct LidGuardHelpCommand(
    string Synopsis,
    string Description,
    IReadOnlyList<LidGuardHelpOption> Options,
    IReadOnlyList<string> Notes);

internal readonly record struct LidGuardHelpOption(string Label, string Description);

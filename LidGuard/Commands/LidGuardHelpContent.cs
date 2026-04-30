using LidGuard.Ipc;
using LidGuard.Mcp;

namespace LidGuard.Commands;

internal static class LidGuardHelpContent
{
    public static IReadOnlyList<LidGuardHelpSection> CreateSections(
        string commandDisplayName,
        string settingsFilePath,
        string sessionLogFilePath,
        string supportedPostStopSuspendSystemSounds)
    {
        return
        [
            CreateUsageSection(commandDisplayName),
            CreateSessionControlSection(commandDisplayName),
            CreateSettingsAndSuspendSection(commandDisplayName, supportedPostStopSuspendSystemSounds),
            CreateDiagnosticsSection(commandDisplayName),
            CreateHookIntegrationSection(commandDisplayName),
            CreateMcpIntegrationSection(commandDisplayName),
            CreateManagedAndInternalCommandsSection(commandDisplayName),
            CreatePathsAndNotesSection(settingsFilePath, sessionLogFilePath)
        ];
    }

    private static LidGuardHelpSection CreateUsageSection(string commandDisplayName)
    {
        return new LidGuardHelpSection(
            "Usage",
            [
                $"{commandDisplayName} <command> [options]",
                "Use --name value or --name=value for options.",
                "Boolean options accept true/false, yes/no, on/off, and 1/0.",
                "Quote paths or text values when they contain spaces."
            ],
            []);
    }

    private static LidGuardHelpSection CreateSessionControlSection(string commandDisplayName)
    {
        return new LidGuardHelpSection(
            "Session Control",
            [],
            [
                new LidGuardHelpCommand(
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
                new LidGuardHelpCommand(
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
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} status",
                    "Show runtime state, active sessions, and effective stored settings.",
                    [],
                    [
                        "If the runtime is not running, status still prints the stored settings file contents."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} cleanup-orphans",
                    "Remove sessions whose watched processes have already exited.",
                    [],
                    [
                        "If the runtime is not running, cleanup-orphans reports that nothing needs cleanup."
                    ])
            ]);
    }

    private static LidGuardHelpSection CreateSettingsAndSuspendSection(string commandDisplayName, string supportedPostStopSuspendSystemSounds)
    {
        return new LidGuardHelpSection(
            "Settings & Suspend",
            [],
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} settings [--reset <bool>] [--change-lid-action <bool>] [--prevent-system-sleep <bool>] [--prevent-away-mode-sleep <bool>] [--prevent-display-sleep <bool>] [--watch-parent-process <bool>] [--emergency-hibernation-on-high-temperature <bool>] [--emergency-hibernation-temperature-mode low|average|high] [--emergency-hibernation-temperature-celsius <number>] [--suspend-mode sleep|hibernate] [--post-stop-suspend-delay-seconds <number>] [--post-stop-suspend-sound off|<system-sound>|<wav-path>] [--pre-suspend-webhook-url <http-or-https-url>] [--closed-lid-permission-request-decision deny|allow] [--power-request-reason <text>]",
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
                        new LidGuardHelpOption("--pre-suspend-webhook-url <http-or-https-url>", "Set the absolute HTTP or HTTPS webhook called before suspend."),
                        new LidGuardHelpOption("--closed-lid-permission-request-decision deny|allow", "Choose how closed-lid PermissionRequest hooks respond when the runtime reports the lid is closed."),
                        new LidGuardHelpOption("--power-request-reason <text>", "Set the power request reason text shown to Windows.")
                    ],
                    [
                        "Running settings with no options enters interactive edit mode.",
                        "Emergency Hibernation temperature mode defaults to Average.",
                        "Emergency Hibernation temperature defaults to 93 Celsius and is clamped to 70 through 110 at runtime.",
                        "Post-stop suspend delay defaults to 10 seconds.",
                        "Use remove-pre-suspend-webhook to clear a configured webhook URL instead of passing off or an empty value."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} remove-pre-suspend-webhook",
                    "Clear the persisted pre-suspend webhook URL.",
                    [],
                    [
                        "This command does not accept any options."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} preview-system-sound --name Asterisk|Beep|Exclamation|Hand|Question",
                    "Play one supported SystemSound name immediately so you can preview it for the post-stop suspend setting.",
                    [
                        new LidGuardHelpOption("--name <sound>", "Required. Allowed values: Asterisk, Beep, Exclamation, Hand, or Question.")
                    ],
                    [])
            ]);
    }

    private static LidGuardHelpSection CreateDiagnosticsSection(string commandDisplayName)
    {
        return new LidGuardHelpSection(
            "Diagnostics",
            [],
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LidGuardPipeCommands.CurrentLidState}",
                    "Report the current lid switch state using the same Windows lid-state source LidGuard uses for closed-lid policy decisions.",
                    [],
                    [
                        "This reports Open, Closed, or Unknown based on the current `GUID_LIDSWITCH_STATE_CHANGE` value."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LidGuardPipeCommands.CurrentMonitorCount}",
                    "Report the current desktop-visible monitor count using the same Windows monitor visibility check LidGuard uses for closed-lid policy decisions.",
                    [],
                    [
                        "This uses `GetSystemMetrics(SM_CMONITORS)` and reports the number of monitors currently attached to the desktop."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LidGuardPipeCommands.CurrentTemperature} [--temperature-mode default|low|average|high]",
                    "Report the current recognized system thermal-zone temperature in Celsius using the selected aggregation mode.",
                    [
                        new LidGuardHelpOption("--temperature-mode default|low|average|high", "Optional. Use the saved LidGuard setting with default, or override it with low, average, or high for this command only.")
                    ],
                    [
                        "If Windows does not currently expose thermal-zone temperature data, the command reports that the value is unavailable.",
                        "When the settings file does not exist yet, default uses LidGuard's headless runtime default mode: Average."
                    ])
            ]);
    }

    private static LidGuardHelpSection CreateHookIntegrationSection(string commandDisplayName)
    {
        return new LidGuardHelpSection(
            "Hook Integration",
            [],
            [
                new LidGuardHelpCommand(
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
                new LidGuardHelpCommand(
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
                new LidGuardHelpCommand(
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
                new LidGuardHelpCommand(
                    $"{commandDisplayName} hook-events [--provider codex|claude|copilot|all] [--count <number>]",
                    "Print recent hook event log lines for the selected provider or providers.",
                    [
                        new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."),
                        new LidGuardHelpOption("--count <number>", "Optional positive line count. Defaults to 50.")
                    ],
                    [
                        "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} codex-hooks [--format config-toml|hooks-json]",
                    "Print a managed Codex hook configuration snippet.",
                    [
                        new LidGuardHelpOption("--format <format>", "Optional. Defaults to config-toml. Also accepts toml or hooks-json.")
                    ],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} claude-hooks [--format settings-json|hooks-json]",
                    "Print a managed Claude Code hook configuration snippet.",
                    [
                        new LidGuardHelpOption("--format <format>", "Optional. Defaults to settings-json. Also accepts json or hooks-json.")
                    ],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} copilot-hooks [--format config-json|hooks-json]",
                    "Print a managed GitHub Copilot CLI hook configuration snippet.",
                    [
                        new LidGuardHelpOption("--format <format>", "Optional. Defaults to config-json. Also accepts json or hooks-json.")
                    ],
                    [])
            ]);
    }

    private static LidGuardHelpSection CreateMcpIntegrationSection(string commandDisplayName)
    {
        return new LidGuardHelpSection(
            "MCP Integration",
            [],
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} mcp-status [--provider codex|claude|copilot|all]",
                    "Inspect the managed user/global LidGuard MCP server registration for one provider or every detected provider.",
                    [
                        new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
                    ],
                    [
                        "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} mcp-install [--provider codex|claude|copilot|all]",
                    "Register or refresh the managed stdio MCP server named lidguard with the selected provider CLI.",
                    [
                        new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
                    ],
                    [
                        "If an existing managed LidGuard MCP server is found, mcp-install removes it first and then installs the current command.",
                        "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} mcp-remove [--provider codex|claude|copilot|all]",
                    "Remove the managed stdio MCP server named lidguard from the selected provider CLI.",
                    [
                        new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
                    ],
                    [
                        "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpStatus} --config <json-path> [--server-name <name>]",
                    "Inspect a caller-supplied JSON configuration file for a managed provider MCP server entry.",
                    [
                        new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to inspect."),
                        new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
                    ],
                    []),
                new LidGuardHelpCommand(
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
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpRemove} --config <json-path> [--server-name <name>]",
                    "Remove a managed provider MCP server entry from a caller-supplied JSON configuration file.",
                    [
                        new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to update."),
                        new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
                    ],
                    [])
            ]);
    }

    private static LidGuardHelpSection CreateManagedAndInternalCommandsSection(string commandDisplayName)
    {
        return new LidGuardHelpSection(
            "Managed / Internal Commands",
            [
                "These commands are intended for provider-managed integrations and stdio hosts rather than direct everyday CLI use."
            ],
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LidGuardMcpServerCommand.CommandName}",
                    "Host the regular LidGuard stdio MCP server that exposes settings and session management tools.",
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {ProviderMcpServerCommand.CommandName} --provider-name <name>",
                    "Host the dedicated provider MCP stdio server for a single caller-supplied provider name.",
                    [
                        new LidGuardHelpOption("--provider-name <name>", "Required provider name exposed to the provider MCP tools.")
                    ],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} codex-hook",
                    "Read Codex hook JSON from standard input and forward start, stop, or closed-lid permission decisions to the runtime.",
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} claude-hook",
                    "Read Claude Code hook JSON from standard input and forward start, stop, activity, soft-lock, elicitation, or permission decisions to the runtime.",
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} copilot-hook --event <event-name>",
                    "Read GitHub Copilot CLI hook JSON from standard input for one configured event name.",
                    [
                        new LidGuardHelpOption("--event <event-name>", "Required. Typical values include sessionStart, sessionEnd, userPromptSubmitted, preToolUse, postToolUse, permissionRequest, agentStop, errorOccurred, and notification.")
                    ],
                    [])
            ]);
    }

    private static LidGuardHelpSection CreatePathsAndNotesSection(string settingsFilePath, string sessionLogFilePath)
    {
        return new LidGuardHelpSection(
            "Paths & Notes",
            [
                $"Settings file: {settingsFilePath}",
                $"Session log: {sessionLogFilePath}",
                "Windows runtime behavior is implemented today. macOS and Linux currently print a support-planned message and exit successfully.",
                "Provider MCP integrations are best-effort only because correct behavior depends on the model calling the LidGuard MCP tools at the right times."
            ],
            []);
    }
}

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

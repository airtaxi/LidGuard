using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class LidGuardHelpTextLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> s_resourceNamesByNeutralText = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Use --name value or --name=value for options."] = "Help_Usage_OptionSyntax",
        ["Boolean options accept true/false, yes/no, on/off, and 1/0."] = "Help_Usage_BooleanOptions",
        ["Quote paths or text values when they contain spaces."] = "Help_Usage_QuoteValues",
        ["These commands are intended for provider-managed integrations and stdio hosts rather than direct everyday CLI use."] = "Help_ManagedCommands_Detail",
        ["Linux support is implemented for systemd/logind systems. macOS support is implemented in macOS builds."] = "Help_Paths_LinuxRuntimeBehavior",
        ["macOS support uses caffeinate and pmset. Windows and Linux support is implemented in their platform builds."] = "Help_Paths_MacOSRuntimeBehavior",
        ["This build includes Windows support. Linux and macOS support is implemented in their platform builds."] = "Help_Paths_WindowsRuntimeBehavior",
        ["Provider MCP integrations are best-effort only because correct behavior depends on the model calling the LidGuard MCP tools at the right times."] = "Help_Paths_ProviderMcpBestEffort",
        ["Read Claude Code hook JSON from standard input and forward start, stop, activity, soft-lock, elicitation, or permission decisions to LidGuard."] = "Help_ClaudeHook_Description",
        ["Print a managed Claude Code hook configuration snippet."] = "Help_ClaudeHooks_Description",
        ["Optional positional value. Defaults to settings-json. Accepts settings-json, json, or hooks-json."] = "Help_ClaudeHooks_FormatOption",
        ["Remove sessions whose watched processes have already exited."] = "Help_CleanupOrphans_Description",
        ["If LidGuard is not running, cleanup-orphans reports that nothing needs cleanup."] = "Help_CleanupOrphans_RuntimeNotRunningNote",
        ["Read Codex hook JSON from standard input and forward start, stop, or closed-lid permission decisions to the runtime."] = "Help_CodexHook_Description",
        ["Print a managed Codex hook configuration snippet."] = "Help_CodexHooks_Description",
        ["Optional positional value. Defaults to config-toml. Accepts config-toml, toml, hooks-json, or json."] = "Help_CodexHooks_FormatOption",
        ["Read GitHub Copilot CLI hook JSON from standard input for one configured event name."] = "Help_CopilotHook_Description",
        ["Required. Typical values include sessionStart, sessionEnd, userPromptSubmitted, preToolUse, postToolUse, permissionRequest, agentStop, errorOccurred, and notification."] = "Help_CopilotHook_EventOption",
        ["Print a managed GitHub Copilot CLI hook configuration snippet."] = "Help_CopilotHooks_Description",
        ["Optional positional value. Defaults to config-json. Accepts config-json, json, or hooks-json."] = "Help_CopilotHooks_FormatOption",
        ["Report the current lid switch state using the same platform lid-state source LidGuard uses for closed-lid policy decisions."] = "Help_CurrentLidState_Description",
        ["This reports Open, Closed, or Unknown based on the current platform lid-state source."] = "Help_CurrentLidState_StateNote",
        ["Report the current visible display monitor count using the same base platform monitor visibility check LidGuard uses for closed-lid policy decisions."] = "Help_CurrentMonitorCount_Description",
        ["Internal laptop panel connections are only excluded by the final suspend eligibility check."] = "Help_CurrentMonitorCount_InternalPanelNote",
        ["Report the current system temperature in Celsius using the selected temperature mode."] = "Help_CurrentTemperature_Description",
        ["Optional positional value. Use default to follow the saved setting, or choose low, average, or high for this command only."] = "Help_CurrentTemperature_TemperatureModeOption",
        ["If no supported temperature sensor data is available on this platform, the command reports that the value is unavailable."] = "Help_CurrentTemperature_UnavailableNote",
        ["When the settings file does not exist yet, default uses Average."] = "Help_CurrentTemperature_DefaultModeNote",
        ["Show the categorized command overview or focused detailed help for one known command or alias."] = "Help_Help_Description",
        ["The <command> --help form uses the same command metadata."] = "Help_Help_CommandOptionNote",
        ["Print recent hook event log lines for the selected provider or providers."] = "Help_HookEvents_Description",
        ["Optional positive line count. Defaults to 50."] = "Help_HookEvents_CountOption",
        ["Install the managed provider hook entries into the selected configuration file."] = "Help_HookInstall_Description",
        ["Remove the managed provider hook entries from the selected configuration file."] = "Help_HookRemove_Description",
        ["Inspect the managed hook configuration for one provider or every detected provider."] = "Help_HookStatus_Description",
        ["Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."] = "Help_ManagedProvider_ProviderOption",
        ["Optional positional value. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."] = "Help_Mcp_ProviderArgument",
        ["Optional provider-specific configuration file override."] = "Help_ManagedProvider_ConfigOption",
        ["Do not combine --config with --provider all because each provider uses a different configuration file."] = "Help_ManagedProvider_ConfigAllNote",
        ["With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."] = "Help_ManagedProvider_AllProvidersNote",
        ["With all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."] = "Help_ManagedProvider_AllProvidersArgumentNote",
        ["Inspect or manage Linux polkit permissions for systemd/logind suspend and inhibitor operations."] = "Help_LinuxPermission_Description",
        ["Print the current Linux permission environment without making changes."] = "Help_LinuxPermission_StatusDescription",
        ["Verify required Linux runtime operations without requesting an actual suspend or hibernate."] = "Help_LinuxPermission_CheckDescription",
        ["Install a LidGuard-managed polkit rule for the current user."] = "Help_LinuxPermission_InstallDescription",
        ["This subcommand uses sudo for the one-time administrator write to /etc/polkit-1/rules.d/49-lidguard.rules."] = "Help_LinuxPermission_InstallNote",
        ["Remove the LidGuard-managed polkit rule when that exact managed rule file is present."] = "Help_LinuxPermission_RemoveDescription",
        ["The rule file is not removed if it does not contain LidGuard's managed markers."] = "Help_LinuxPermission_RemoveNote",
        ["Inspect or manage macOS sudoers permissions for pmset and powermetrics operations."] = "Help_MacOSPermission_Description",
        ["Print the current macOS permission environment without making changes."] = "Help_MacOSPermission_StatusDescription",
        ["Verify required macOS runtime operations without requesting an actual sleep or hibernate."] = "Help_MacOSPermission_CheckDescription",
        ["Install a LidGuard-managed sudoers rule for the current user."] = "Help_MacOSPermission_InstallDescription",
        ["This subcommand uses sudo for the one-time administrator write to /private/etc/sudoers.d/lidguard."] = "Help_MacOSPermission_InstallNote",
        ["The managed rule permits only LidGuard's pmset disablesleep, pmset hibernatemode, and powermetrics SMC sample commands."] = "Help_MacOSPermission_ManagedRuleNote",
        ["Remove the LidGuard-managed sudoers rule when that exact managed rule file is present."] = "Help_MacOSPermission_RemoveDescription",
        ["The sudoers file is not removed if it does not contain LidGuard's managed markers."] = "Help_MacOSPermission_RemoveNote",
        ["Register or refresh the managed stdio MCP server named lidguard with the selected provider CLI."] = "Help_McpInstall_Description",
        ["If an existing managed LidGuard MCP server is found, mcp-install removes it first and then installs the current command."] = "Help_McpInstall_RefreshNote",
        ["Remove the managed stdio MCP server named lidguard from the selected provider CLI."] = "Help_McpRemove_Description",
        ["Host the regular LidGuard stdio MCP server that exposes settings and session management tools."] = "Help_McpServer_Description",
        ["Inspect the managed user/global LidGuard MCP server registration for one provider or every detected provider."] = "Help_McpStatus_Description",
        ["Play the saved sleep or hibernate warning sound now, using the saved temporary volume setting."] = "Help_PreviewCurrentSound_Description",
        ["If no warning sound is configured, this command prints settings guidance instead of failing."] = "Help_PreviewCurrentSound_NoSoundNote",
        ["This command waits until playback finishes."] = "Help_SoundPreview_WaitsForPlaybackNote",
        ["Play one supported SystemSound name now, using the saved temporary volume setting."] = "Help_PreviewSystemSound_Description",
        ["Required positional value. Allowed values: Asterisk, Beep, Exclamation, Hand, or Question."] = "Help_PreviewSystemSound_NameOption",
        ["Write or update a managed provider MCP stdio server entry in a caller-supplied JSON configuration file."] = "Help_ProviderMcpInstall_Description",
        ["Required. JSON configuration file to create or update."] = "Help_ProviderMcpInstall_ConfigOption",
        ["Required provider name passed through to provider-mcp-server."] = "Help_ProviderMcpInstall_ProviderNameOption",
        ["This path edits the supplied JSON file directly and does not call provider-specific mcp add/remove commands."] = "Help_ProviderMcpInstall_DirectEditNote",
        ["Remove a managed provider MCP server entry from a caller-supplied JSON configuration file."] = "Help_ProviderMcpRemove_Description",
        ["Required. JSON configuration file to update."] = "Help_ProviderMcpRemove_ConfigOption",
        ["Host the dedicated provider MCP stdio server for a single caller-supplied provider name."] = "Help_ProviderMcpServer_Description",
        ["Required provider name exposed to the provider MCP tools."] = "Help_ProviderMcpServer_ProviderNameOption",
        ["Inspect a caller-supplied JSON configuration file for a managed provider MCP server entry."] = "Help_ProviderMcpStatus_Description",
        ["Required. JSON configuration file to inspect."] = "Help_ProviderMcpStatus_ConfigOption",
        ["Optional managed server entry name. Defaults to lidguard-provider."] = "Help_ProviderMcp_ServerNameOption",
        ["Clear the saved session completion webhook URL."] = "Help_RemovePostSessionEndWebhook_Description",
        ["Clear the saved before sleep or hibernate webhook URL."] = "Help_RemovePreSuspendWebhook_Description",
        ["This command does not accept any options."] = "Help_Command_NoOptionsNote",
        ["Remove active sessions currently tracked by the runtime."] = "Help_RemoveSession_Description",
        ["Remove every active session currently tracked by the runtime."] = "Help_RemoveSession_AllDescription",
        ["--all cannot be combined with --session, --provider, or --provider-name."] = "Help_RemoveSession_AllCannotCombineNote",
        ["Remove active sessions by session identifier without waiting for provider stop hooks."] = "Help_RemoveSession_ByIdentifierDescription",
        ["Required. Session identifier to remove."] = "Help_RemoveSession_SessionOption",
        ["Optional. Narrows removal to one provider. Allowed values: codex, claude, copilot, custom, mcp, or unknown."] = "Help_RemoveSession_ProviderOption",
        ["Optional. Narrows removal to one MCP provider name when --provider mcp is used."] = "Help_RemoveSession_ProviderNameOption",
        ["When --provider is omitted, LidGuard removes every active session whose session identifier matches."] = "Help_RemoveSession_NoProviderNote",
        ["When --provider mcp is used without --provider-name, LidGuard removes every MCP-backed session with the same session identifier."] = "Help_RemoveSession_McpWithoutProviderNameNote",
        ["Show and update the default settings used when sessions start."] = "Help_Settings_Description",
        ["Running settings with no options enters interactive edit mode."] = "Help_Settings_InteractiveModeNote",
        ["Use remove-pre-suspend-webhook or remove-post-session-end-webhook to clear webhook URLs; webhook URL options do not accept off or empty values."] = "Help_Settings_RemoveWebhookNote",
        ["When true, reset settings to LidGuard defaults before applying the values in this command."] = "Help_Settings_ResetOption",
        ["Change whether LidGuard temporarily sets the active power plan's lid close action to Do Nothing."] = "Help_Settings_ChangeLidActionOption",
        ["Change whether LidGuard prevents normal system sleep while sessions are active."] = "Help_Settings_PreventSystemSleepOption",
        ["Change whether Windows keeps background work running with Away Mode when supported."] = "Help_Settings_PreventAwayModeSleepOption",
        ["Change whether LidGuard prevents the display from sleeping while sessions are active."] = "Help_Settings_PreventDisplaySleepOption",
        ["Change the process monitoring policy for tracked sessions."] = "Help_Settings_WatchParentProcessOption",
        ["Turn off the inactive-session timeout, or let idle sessions stop keeping the system awake after this many minutes without activity. Minimum enabled value is 1."] = "Help_Settings_SessionTimeoutOption",
        ["Set how long LidGuard stays running after all sessions end and cleanup is finished. Pass off to keep the runtime alive, 0 to exit immediately, or a positive minute count to wait."] = "Help_Settings_ServerRuntimeCleanupDelayOption",
        ["Change whether LidGuard requests Emergency Hibernation when the guarded system temperature reaches the configured threshold while the lid is closed."] = "Help_Settings_EmergencyHibernationOption",
        ["Choose whether LidGuard uses the lowest, average, or highest available temperature sensor value."] = "Help_Settings_EmergencyHibernationTemperatureModeOption",
        ["Set the Emergency Hibernation threshold in Celsius. Allowed range: 70 through 110."] = "Help_Settings_EmergencyHibernationTemperatureCelsiusOption",
        ["Choose whether LidGuard requests sleep or hibernate after the last active session ends or stops keeping the system awake."] = "Help_Settings_SuspendModeOption",
        ["Set the delay before sleep or hibernate after sessions end. Use 0 for immediate sleep or hibernate."] = "Help_Settings_PostStopSuspendDelayOption",
        ["Disable the sleep or hibernate warning sound, use one supported SystemSound name ({supportedPostStopSuspendSystemSounds}), or supply an existing playable .wav path."] = "Help_Settings_PostStopSuspendSoundOption",
        ["Disable the temporary volume change, or set the default output volume while the sleep or hibernate warning sound plays. LidGuard restores the previous volume and mute state afterward."] = "Help_Settings_PostStopSuspendSoundVolumeOption",
        ["Disable sleep/hibernate history recording or retain the most recent sleep/hibernate request entries. Minimum enabled value is 1."] = "Help_Settings_SuspendHistoryOption",
        ["Set the webhook URL called before LidGuard requests sleep or hibernate."] = "Help_Settings_PreSuspendWebhookOption",
        ["Set the webhook URL called after a session completes normally when LidGuard is not about to sleep or hibernate."] = "Help_Settings_PostSessionEndWebhookOption",
        ["Choose how closed-lid PermissionRequest hooks respond when LidGuard reports the lid is closed."] = "Help_Settings_ClosedLidPermissionRequestDecisionOption",
        ["Set the CLI display language/culture. auto follows the current process or operating system setting, and LIDGUARD_UI_CULTURE takes priority over the saved value."] = "Help_Settings_UserInterfaceCultureOption",
        ["Set the reason text Windows shows for LidGuard's sleep prevention."] = "Help_Settings_PowerRequestReasonOption",
        ["Start or refresh a tracked session and load the saved default settings."] = "Help_Start_Description",
        ["Required. Allowed values: codex, claude, copilot, custom, or mcp."] = "Help_Session_ProviderOption",
        ["Optional. Session identifier to track. When omitted, LidGuard derives one from the provider display name and normalized working directory."] = "Help_Start_SessionOption",
        ["Required when --provider mcp is used. Distinguishes one MCP-backed provider from another."] = "Help_Start_ProviderNameOption",
        ["Optional process ID to monitor so LidGuard can clean up if that process exits."] = "Help_Start_ParentProcessOption",
        ["Optional working directory used for fallback session identity and process resolution. Defaults to the current directory."] = "Help_Start_WorkingDirectoryOption",
        ["If no runtime is listening, start launches the detached runtime server automatically."] = "Help_Start_RuntimeLaunchNote",
        ["Show runtime state, active sessions, and effective stored settings."] = "Help_Status_Description",
        ["If LidGuard is not running, status still prints the stored settings file contents."] = "Help_Status_RuntimeNotRunningNote",
        ["Show a live terminal dashboard for runtime state, active sessions, and recent LidGuard flow logs."] = "Help_LiveStatus_Description",
        ["This command does not start the runtime; it waits and reconnects when the runtime is unavailable."] = "Help_LiveStatus_RuntimeNotStartedNote",
        ["Press q, Escape, or Ctrl+C to exit."] = "Help_LiveStatus_ExitNote",
        ["Stop a tracked session by matching the same provider and session identity used when the session started."] = "Help_Stop_Description",
        ["Optional. When omitted, LidGuard uses the same fallback session identifier strategy as start."] = "Help_Stop_SessionOption",
        ["Required when --provider mcp is used."] = "Help_Stop_ProviderNameOption",
        ["Optional process ID to match when stopping a tracked session."] = "Help_Stop_ParentProcessOption",
        ["Optional working directory used for fallback session identity. Defaults to the current directory."] = "Help_Stop_WorkingDirectoryOption",
        ["Print recent sleep/hibernate request history from the local history log."] = "Help_SuspendHistory_Description",
        ["Optional positive entry count to display. Defaults to the saved suspend-history-count value, or 10 when recording is off."] = "Help_SuspendHistory_CountOption",
        ["The saved suspend-history-count setting controls how many entries are retained. The count argument only limits how many retained entries are displayed."] = "Help_SuspendHistory_CountNote"
    };

    internal static LidGuardHelpSection LocalizeSection(LidGuardHelpSection helpSection)
    {
        return new LidGuardHelpSection(
            helpSection.Title,
            LocalizeStrings(helpSection.Details),
            LocalizeCommands(helpSection.Commands));
    }

    internal static string Localize(string neutralText)
    {
        if (!s_resourceNamesByNeutralText.TryGetValue(neutralText, out var resourceName)) return neutralText;
        return LidGuardText.GetResourceString(resourceName, neutralText);
    }

    private static IReadOnlyList<LidGuardHelpCommand> LocalizeCommands(IReadOnlyList<LidGuardHelpCommand> helpCommands)
    {
        var localizedCommands = new List<LidGuardHelpCommand>();
        foreach (var helpCommand in helpCommands)
        {
            localizedCommands.Add(new LidGuardHelpCommand(
                helpCommand.Synopsis,
                Localize(helpCommand.Description),
                LocalizeOptions(helpCommand.Options),
                LocalizeStrings(helpCommand.Notes)));
        }

        return localizedCommands;
    }

    private static IReadOnlyList<LidGuardHelpOption> LocalizeOptions(IReadOnlyList<LidGuardHelpOption> helpOptions)
    {
        var localizedOptions = new List<LidGuardHelpOption>();
        foreach (var helpOption in helpOptions) localizedOptions.Add(new LidGuardHelpOption(helpOption.Label, Localize(helpOption.Description)));
        return localizedOptions;
    }

    private static IReadOnlyList<string> LocalizeStrings(IReadOnlyList<string> neutralTexts)
    {
        var localizedStrings = new List<string>();
        foreach (var neutralText in neutralTexts) localizedStrings.Add(Localize(neutralText));
        return localizedStrings;
    }
}

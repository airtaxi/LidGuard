using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class SettingsHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        var supportedPostStopSuspendSystemSounds = context.SupportedPostStopSuspendSystemSounds;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.Settings,
            [],
            LidGuardHelpSectionTitles.SettingsAndSuspend,
            $"{commandDisplayName} settings [--reset <bool>] [--change-lid-action <bool>] [--prevent-system-sleep <bool>] [--prevent-away-mode-sleep <bool>] [--prevent-display-sleep <bool>] [--watch-parent-process <bool>] [--session-timeout-minutes off|<minutes>] [--server-runtime-cleanup-delay-minutes off|<minutes>] [--emergency-hibernation-on-high-temperature <bool>] [--emergency-hibernation-temperature-mode low|average|high] [--emergency-hibernation-temperature-celsius <number>] [--suspend-mode sleep|hibernate] [--post-stop-suspend-delay-seconds <number>] [--post-stop-suspend-sound off|<system-sound>|<wav-path>] [--post-stop-suspend-sound-volume-override-percent off|<1-100>] [--suspend-history-count off|<count>] [--pre-suspend-webhook-url <http-or-https-url>] [--closed-lid-permission-request-decision deny|allow] [--power-request-reason <text>]",
            "Show and update the persisted default settings used by start and hook-driven runtime requests.",
            [
                new LidGuardHelpOption("--reset <bool>", "When true, start from headless runtime defaults before applying the other supplied options."),
                new LidGuardHelpOption("--change-lid-action <bool>", "Toggle the temporary active power plan lid close action override."),
                new LidGuardHelpOption("--prevent-system-sleep <bool>", "Toggle PowerRequestSystemRequired handling."),
                new LidGuardHelpOption("--prevent-away-mode-sleep <bool>", "Toggle PowerRequestAwayModeRequired handling."),
                new LidGuardHelpOption("--prevent-display-sleep <bool>", "Toggle PowerRequestDisplayRequired handling."),
                new LidGuardHelpOption("--watch-parent-process <bool>", "Toggle the process exit watchdog for tracked sessions."),
                new LidGuardHelpOption("--session-timeout-minutes off|<minutes>", "Disable inactive session timeout soft-locking or transition sessions to soft-locked after this many minutes since their last activity. Minimum enabled value is 1."),
                new LidGuardHelpOption("--server-runtime-cleanup-delay-minutes off|<minutes>", "Set how long the server runtime stays alive after all sessions are gone and pending cleanup is finished. Pass off to exit immediately. Minimum enabled value is 1."),
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
                "Session timeout defaults to 12 minutes; pass off to disable inactive timeout soft-locking, or a value of 1 or more to soft-lock inactive sessions.",
                "Server runtime cleanup delay defaults to 10 minutes after the last session and pending cleanup finish; pass off to exit immediately.",
                "Post-stop suspend delay defaults to 10 seconds.",
                "Post-stop suspend sound volume override defaults to off; pass off to disable it.",
                "Suspend history recording defaults to on and keeps the latest 10 entries.",
                "Use remove-pre-suspend-webhook to clear a configured webhook URL instead of passing off or an empty value."
            ]);
    }
}

using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class SettingsHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        var supportedPostStopSuspendSystemSounds = context.SupportedPostStopSuspendSystemSounds;
        var synopsis = CreateSynopsis(commandDisplayName);
        var options = CreateOptions(supportedPostStopSuspendSystemSounds);
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.Settings,
            [],
            LidGuardHelpSectionTitles.SettingsAndSuspend,
            synopsis,
            "Show and update the default settings used when sessions start.",
            options,
            [
                "Running settings with no options enters interactive edit mode.",
                "Use remove-pre-suspend-webhook or remove-post-session-end-webhook to clear webhook URLs; webhook URL options do not accept off or empty values."
            ]);
    }

    private static string CreateSynopsis(string commandDisplayName)
    {
        var powerRequestOptions = "[--prevent-system-sleep <bool>]";
#if !LIDGUARD_LINUX && !LIDGUARD_MACOS
        powerRequestOptions += " [--prevent-away-mode-sleep <bool>]";
#endif

        return $"{commandDisplayName} settings [--reset <bool>] [--change-lid-action <bool>] {powerRequestOptions} [--prevent-display-sleep <bool>] [--watch-parent-process <bool>] [--session-timeout-minutes off|<minutes>] [--server-runtime-cleanup-delay-minutes off|0|<minutes>] [--emergency-hibernation-on-high-temperature <bool>] [--emergency-hibernation-temperature-mode low|average|high] [--emergency-hibernation-temperature-celsius <number>] [--suspend-mode sleep|hibernate] [--post-stop-suspend-delay-seconds <number>] [--post-stop-suspend-sound off|<system-sound>|<wav-path>] [--post-stop-suspend-sound-volume-override-percent off|<1-100>] [--suspend-history-count off|<count>] [--pre-suspend-webhook-url <http-or-https-url>] [--post-session-end-webhook-url <http-or-https-url>] [--closed-lid-permission-request-decision deny|allow] [--ui-culture auto|en|ko|<culture-name>] [--power-request-reason <text>]";
    }

    private static IReadOnlyList<LidGuardHelpOption> CreateOptions(string supportedPostStopSuspendSystemSounds)
    {
        var options = new List<LidGuardHelpOption>
        {
            new("--reset <bool>", "When true, reset settings to LidGuard defaults before applying the values in this command."),
            new("--change-lid-action <bool>", "Change whether LidGuard temporarily sets the active power plan's lid close action to Do Nothing."),
            new("--prevent-system-sleep <bool>", "Change whether LidGuard prevents normal system sleep while sessions are active.")
        };
#if !LIDGUARD_LINUX && !LIDGUARD_MACOS
        options.Add(new LidGuardHelpOption("--prevent-away-mode-sleep <bool>", "Change whether Windows keeps background work running with Away Mode when supported."));
#endif
        options.AddRange([
            new LidGuardHelpOption("--prevent-display-sleep <bool>", "Change whether LidGuard prevents the display from sleeping while sessions are active."),
            new LidGuardHelpOption("--watch-parent-process <bool>", "Change the process monitoring policy for tracked sessions."),
            new LidGuardHelpOption("--session-timeout-minutes off|<minutes>", "Turn off the inactive-session timeout, or let idle sessions stop keeping the system awake after this many minutes without activity. Minimum enabled value is 1."),
            new LidGuardHelpOption("--server-runtime-cleanup-delay-minutes off|0|<minutes>", "Set how long LidGuard stays running after all sessions end and cleanup is finished. Pass off to keep the runtime alive, 0 to exit immediately, or a positive minute count to wait."),
            new LidGuardHelpOption("--emergency-hibernation-on-high-temperature <bool>", "Change whether LidGuard requests Emergency Hibernation when the guarded system temperature reaches the configured threshold while the lid is closed."),
            new LidGuardHelpOption("--emergency-hibernation-temperature-mode low|average|high", "Choose whether LidGuard uses the lowest, average, or highest available temperature sensor value."),
            new LidGuardHelpOption("--emergency-hibernation-temperature-celsius <number>", "Set the Emergency Hibernation threshold in Celsius. Allowed range: 70 through 110."),
            new LidGuardHelpOption("--suspend-mode sleep|hibernate", "Choose whether LidGuard requests sleep or hibernate after the last active session ends or stops keeping the system awake."),
            new LidGuardHelpOption("--post-stop-suspend-delay-seconds <number>", "Set the delay before sleep or hibernate after sessions end. Use 0 for immediate sleep or hibernate."),
            new LidGuardHelpOption("--post-stop-suspend-sound off|<system-sound>|<wav-path>", LidGuardText.HelpSettingsPostStopSuspendSoundOption(supportedPostStopSuspendSystemSounds)),
            new LidGuardHelpOption("--post-stop-suspend-sound-volume-override-percent off|<1-100>", "Disable the temporary volume change, or set the default output volume while the sleep or hibernate warning sound plays. LidGuard restores the previous volume and mute state afterward."),
            new LidGuardHelpOption("--suspend-history-count off|<count>", "Disable sleep/hibernate history recording or retain the most recent sleep/hibernate request entries. Minimum enabled value is 1."),
            new LidGuardHelpOption("--pre-suspend-webhook-url <http-or-https-url>", "Set the webhook URL called before LidGuard requests sleep or hibernate."),
            new LidGuardHelpOption("--post-session-end-webhook-url <http-or-https-url>", "Set the webhook URL called after a session completes normally when LidGuard is not about to sleep or hibernate."),
            new LidGuardHelpOption("--closed-lid-permission-request-decision deny|allow", "Choose how closed-lid PermissionRequest hooks respond when LidGuard reports the lid is closed."),
            new LidGuardHelpOption("--ui-culture auto|en|ko|<culture-name>", "Set the CLI display language/culture. auto follows the current process or operating system setting, and LIDGUARD_UI_CULTURE takes priority over the saved value."),
            new LidGuardHelpOption("--power-request-reason <text>", "Set the reason text Windows shows for LidGuard's sleep prevention.")
        ]);

        return options;
    }
}

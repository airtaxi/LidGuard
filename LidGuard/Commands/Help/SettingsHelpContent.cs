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
            LocalizationService.GetString("Help_Settings_Description"),
            options,
            [
                LocalizationService.GetString("Help_Settings_InteractiveModeNote"),
                LocalizationService.GetString("Help_Settings_RemoveWebhookNote")
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
            new("--reset <bool>", LocalizationService.GetString("Help_Settings_ResetOption")),
            new("--change-lid-action <bool>", LocalizationService.GetString("Help_Settings_ChangeLidActionOption")),
            new("--prevent-system-sleep <bool>", LocalizationService.GetString("Help_Settings_PreventSystemSleepOption"))
        };
#if !LIDGUARD_LINUX && !LIDGUARD_MACOS
        options.Add(new LidGuardHelpOption("--prevent-away-mode-sleep <bool>", LocalizationService.GetString("Help_Settings_PreventAwayModeSleepOption")));
#endif
        options.AddRange([
            new LidGuardHelpOption("--prevent-display-sleep <bool>", LocalizationService.GetString("Help_Settings_PreventDisplaySleepOption")),
            new LidGuardHelpOption("--watch-parent-process <bool>", LocalizationService.GetString("Help_Settings_WatchParentProcessOption")),
            new LidGuardHelpOption("--session-timeout-minutes off|<minutes>", LocalizationService.GetString("Help_Settings_SessionTimeoutOption")),
            new LidGuardHelpOption("--server-runtime-cleanup-delay-minutes off|0|<minutes>", LocalizationService.GetString("Help_Settings_ServerRuntimeCleanupDelayOption")),
            new LidGuardHelpOption("--emergency-hibernation-on-high-temperature <bool>", LocalizationService.GetString("Help_Settings_EmergencyHibernationOption")),
            new LidGuardHelpOption("--emergency-hibernation-temperature-mode low|average|high", LocalizationService.GetString("Help_Settings_EmergencyHibernationTemperatureModeOption")),
            new LidGuardHelpOption("--emergency-hibernation-temperature-celsius <number>", LocalizationService.GetString("Help_Settings_EmergencyHibernationTemperatureCelsiusOption")),
            new LidGuardHelpOption("--suspend-mode sleep|hibernate", LocalizationService.GetString("Help_Settings_SuspendModeOption")),
            new LidGuardHelpOption("--post-stop-suspend-delay-seconds <number>", LocalizationService.GetString("Help_Settings_PostStopSuspendDelayOption")),
            new LidGuardHelpOption("--post-stop-suspend-sound off|<system-sound>|<wav-path>", LocalizationService.GetFormattedString("HelpSettingsPostStopSuspendSoundOption", supportedPostStopSuspendSystemSounds)),
            new LidGuardHelpOption("--post-stop-suspend-sound-volume-override-percent off|<1-100>", LocalizationService.GetString("Help_Settings_PostStopSuspendSoundVolumeOption")),
            new LidGuardHelpOption("--suspend-history-count off|<count>", LocalizationService.GetString("Help_Settings_SuspendHistoryOption")),
            new LidGuardHelpOption("--pre-suspend-webhook-url <http-or-https-url>", LocalizationService.GetString("Help_Settings_PreSuspendWebhookOption")),
            new LidGuardHelpOption("--post-session-end-webhook-url <http-or-https-url>", LocalizationService.GetString("Help_Settings_PostSessionEndWebhookOption")),
            new LidGuardHelpOption("--closed-lid-permission-request-decision deny|allow", LocalizationService.GetString("Help_Settings_ClosedLidPermissionRequestDecisionOption")),
            new LidGuardHelpOption("--ui-culture auto|en|ko|<culture-name>", LocalizationService.GetString("Help_Settings_UserInterfaceCultureOption")),
            new LidGuardHelpOption("--power-request-reason <text>", LocalizationService.GetString("Help_Settings_PowerRequestReasonOption"))
        ]);

        return options;
    }
}

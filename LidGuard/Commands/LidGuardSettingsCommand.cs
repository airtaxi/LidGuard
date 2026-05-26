using LidGuard.Platform;
using LidGuard.Settings;

namespace LidGuard.Commands;

internal static class LidGuardSettingsCommand
{
    public static Task<int> SendSettingsAsync(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform) => LidGuardSettingsUpdateCommand.SendSettingsAsync(options, runtimePlatform);

    public static Task<int> SendRemovePreSuspendWebhookAsync(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform) => LidGuardPreSuspendWebhookRemovalCommand.SendRemovePreSuspendWebhookAsync(options, runtimePlatform);

    public static Task<int> SendRemovePostSessionEndWebhookAsync(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform) => LidGuardPostSessionEndWebhookRemovalCommand.SendRemovePostSessionEndWebhookAsync(options, runtimePlatform);

    public static Task<int> SendRemoveClosedLidStopFollowUpWebhookAsync(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform) => LidGuardClosedLidStopFollowUpWebhookRemovalCommand.SendRemoveClosedLidStopFollowUpWebhookAsync(options, runtimePlatform);

    public static int PreviewCurrentSound(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform) => LidGuardSettingsSoundPreviewCommand.PreviewCurrentSound(options, runtimePlatform);

    public static int PreviewSystemSound(string systemSoundName, ILidGuardRuntimePlatform runtimePlatform) => LidGuardSettingsSoundPreviewCommand.PreviewSystemSound(systemSoundName, runtimePlatform);

    public static bool TryParseEmergencyHibernationTemperatureMode(string emergencyHibernationTemperatureModeText, out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
        => LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureMode(emergencyHibernationTemperatureModeText, out emergencyHibernationTemperatureMode);
}

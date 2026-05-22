namespace LidGuard.Settings;

internal static class ClosedLidStopFollowUpWebhookConfiguration
{
    public static string GetDisplayValue(string closedLidStopFollowUpWebhookUrl)
        => ClosedLidStopFollowUpConfiguration.GetDisplayValue(closedLidStopFollowUpWebhookUrl);

    public static LidGuardSettings WithClosedLidStopFollowUpWebhookUrl(LidGuardSettings settings, string closedLidStopFollowUpWebhookUrl)
    {
        var normalizedInputSettings = LidGuardSettings.Normalize(settings);
        return normalizedInputSettings with
        {
            ClosedLidStopFollowUpWebhookUrl = closedLidStopFollowUpWebhookUrl
        };
    }

    public static bool TryNormalizeConfiguredValue(
        string closedLidStopFollowUpWebhookUrl,
        out string normalizedClosedLidStopFollowUpWebhookUrl,
        out string message)
        => ClosedLidStopFollowUpConfiguration.TryNormalizeConfiguredValue(
            closedLidStopFollowUpWebhookUrl,
            out normalizedClosedLidStopFollowUpWebhookUrl,
            out message);
}

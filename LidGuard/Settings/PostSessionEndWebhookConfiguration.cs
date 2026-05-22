namespace LidGuard.Settings;

internal static class PostSessionEndWebhookConfiguration
{
    public static string GetDisplayValue(string postSessionEndWebhookUrl)
        => WebhookUrlConfiguration.GetDisplayValue(postSessionEndWebhookUrl);

    public static LidGuardSettings WithPostSessionEndWebhookUrl(LidGuardSettings settings, string postSessionEndWebhookUrl)
    {
        var normalizedInputSettings = LidGuardSettings.Normalize(settings);
        return normalizedInputSettings with
        {
            PostSessionEndWebhookUrl = postSessionEndWebhookUrl
        };
    }

    public static bool TryNormalizeConfiguredValue(string postSessionEndWebhookUrl, out string normalizedPostSessionEndWebhookUrl, out string message)
        => WebhookUrlConfiguration.TryNormalizeConfiguredValue(
            postSessionEndWebhookUrl,
            "post-session-end",
            out normalizedPostSessionEndWebhookUrl,
            out message);
}

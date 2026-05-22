namespace LidGuard.Settings;

internal static class PreSuspendWebhookConfiguration
{
    public static string GetDisplayValue(string preSuspendWebhookUrl)
        => WebhookUrlConfiguration.GetDisplayValue(preSuspendWebhookUrl);

    public static LidGuardSettings WithPreSuspendWebhookUrl(LidGuardSettings settings, string preSuspendWebhookUrl)
    {
        var normalizedInputSettings = LidGuardSettings.Normalize(settings);
        return normalizedInputSettings with
        {
            PreSuspendWebhookUrl = preSuspendWebhookUrl
        };
    }

    public static bool TryNormalizeConfiguredValue(string preSuspendWebhookUrl, out string normalizedPreSuspendWebhookUrl, out string message)
        => WebhookUrlConfiguration.TryNormalizeConfiguredValue(preSuspendWebhookUrl, "pre-suspend", out normalizedPreSuspendWebhookUrl, out message);
}

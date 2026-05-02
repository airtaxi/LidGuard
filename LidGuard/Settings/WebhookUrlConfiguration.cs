namespace LidGuard.Settings;

internal static class WebhookUrlConfiguration
{
    public static string GetDisplayValue(string webhookUrl)
        => string.IsNullOrWhiteSpace(webhookUrl) ? "off" : webhookUrl;

    public static bool TryNormalizeConfiguredValue(
        string webhookUrl,
        string displayName,
        out string normalizedWebhookUrl,
        out string message)
    {
        normalizedWebhookUrl = string.Empty;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(webhookUrl)) return true;

        var normalizedInput = webhookUrl.Trim();
        if (!Uri.TryCreate(normalizedInput, UriKind.Absolute, out var webhookUri) ||
            (webhookUri.Scheme != Uri.UriSchemeHttp && webhookUri.Scheme != Uri.UriSchemeHttps))
        {
            message = $"The {displayName} webhook URL must be empty or an absolute HTTP or HTTPS URL.";
            return false;
        }

        normalizedWebhookUrl = webhookUri.AbsoluteUri;
        return true;
    }
}

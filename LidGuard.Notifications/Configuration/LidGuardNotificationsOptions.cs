namespace LidGuard.Notifications.Configuration;

internal sealed class LidGuardNotificationsOptions
{
    public const string SectionName = "LidGuardNotifications";

    public string AccessToken { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public string VapidPublicKey { get; set; } = string.Empty;

    public string VapidPrivateKey { get; set; } = string.Empty;

    public string VapidSubject { get; set; } = string.Empty;

    public string DatabasePath { get; set; } = string.Empty;

    public string PublicBaseUrl { get; set; } = string.Empty;

    public void Normalize()
    {
        AccessToken = AccessToken.Trim();
        WebhookSecret = WebhookSecret.Trim();
        VapidPublicKey = VapidPublicKey.Trim();
        VapidPrivateKey = VapidPrivateKey.Trim();
        VapidSubject = VapidSubject.Trim();
        DatabasePath = string.IsNullOrWhiteSpace(DatabasePath) ? GetDefaultDatabasePath() : Environment.ExpandEnvironmentVariables(DatabasePath.Trim());
        PublicBaseUrl = PublicBaseUrl.Trim().TrimEnd('/');
    }

    public bool TryValidate(out string message)
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            message = "AccessToken is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(WebhookSecret))
        {
            message = "WebhookSecret is required.";
            return false;
        }

        if (string.Equals(AccessToken, WebhookSecret, StringComparison.Ordinal))
        {
            message = "AccessToken and WebhookSecret must be different values.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(VapidPublicKey))
        {
            message = "VapidPublicKey is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(VapidPrivateKey))
        {
            message = "VapidPrivateKey is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(VapidSubject))
        {
            message = "VapidSubject is required.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(PublicBaseUrl)
            && (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicBaseUri)
                || (publicBaseUri.Scheme != Uri.UriSchemeHttps
                    && (publicBaseUri.Scheme != Uri.UriSchemeHttp || publicBaseUri.Host != "localhost"))))
        {
            message = "PublicBaseUrl must be an HTTPS URL, or an HTTP localhost URL for development.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static string GetDefaultDatabasePath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rootDirectory = string.IsNullOrWhiteSpace(localApplicationData) ? AppContext.BaseDirectory : localApplicationData;
        return Path.Combine(rootDirectory, "LidGuard", "Notifications", "notifications.sqlite");
    }
}

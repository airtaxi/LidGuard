namespace LidGuard.Notifications.Security;

internal static class LocalRedirectPath
{
    public static string Normalize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return "/";
        if (!returnUrl.StartsWith('/')) return "/";
        if (returnUrl.StartsWith("//", StringComparison.Ordinal)) return "/";
        if (returnUrl.StartsWith("/\\", StringComparison.Ordinal)) return "/";

        return returnUrl;
    }
}

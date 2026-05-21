namespace LidGuard.Notifications.Security;

internal static class DashboardAuthenticationConstants
{
    public const string AccessCookieName = "LidGuard.Notifications.Access";
    public const string RefreshCookieName = "LidGuard.Notifications.Refresh";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);
}

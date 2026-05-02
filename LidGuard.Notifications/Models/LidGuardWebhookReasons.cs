namespace LidGuard.Notifications.Models;

internal static class LidGuardWebhookReasons
{
    public const string Completed = nameof(Completed);
    public const string SoftLocked = nameof(SoftLocked);
    public const string EmergencyHibernation = nameof(EmergencyHibernation);

    public static bool IsRecognized(string reason)
        => string.Equals(reason, Completed, StringComparison.Ordinal)
            || string.Equals(reason, SoftLocked, StringComparison.Ordinal)
            || string.Equals(reason, EmergencyHibernation, StringComparison.Ordinal);
}

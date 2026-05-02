namespace LidGuard.Notifications.Models;

internal static class LidGuardWebhookReasons
{
    public const string Completed = nameof(Completed);
    public const string SoftLocked = nameof(SoftLocked);
    public const string EmergencyHibernation = nameof(EmergencyHibernation);
    public const string SessionEnded = nameof(SessionEnded);

    public static bool IsRecognizedPreSuspendReason(string reason)
        => string.Equals(reason, Completed, StringComparison.Ordinal)
            || string.Equals(reason, SoftLocked, StringComparison.Ordinal)
            || string.Equals(reason, EmergencyHibernation, StringComparison.Ordinal);

    public static bool IsRecognizedPostSessionEndReason(string reason)
        => string.Equals(reason, SessionEnded, StringComparison.Ordinal);
}

namespace LidGuard.Notifications.Data;

internal static class WebhookEventStatuses
{
    public const string Pending = nameof(Pending);
    public const string Processing = nameof(Processing);
    public const string Completed = nameof(Completed);
    public const string Failed = nameof(Failed);
}

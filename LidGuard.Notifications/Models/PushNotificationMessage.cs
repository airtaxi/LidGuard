namespace LidGuard.Notifications.Models;

internal sealed class PushNotificationMessage
{
    public string Title { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string Url { get; init; } = "/";

    public string Tag { get; init; } = "lidguard-suspend";
}

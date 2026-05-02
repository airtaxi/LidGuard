namespace LidGuard.Notifications.Services;

internal enum PushNotificationSendStatus
{
    Succeeded = 0,
    PermanentFailure = 1,
    TransientFailure = 2
}

internal sealed record PushNotificationSendResult(
    PushNotificationSendStatus Status,
    int? HttpStatusCode,
    string? ErrorMessage)
{
    public static PushNotificationSendResult Succeeded()
        => new(PushNotificationSendStatus.Succeeded, null, null);

    public static PushNotificationSendResult PermanentFailure(int? httpStatusCode, string errorMessage)
        => new(PushNotificationSendStatus.PermanentFailure, httpStatusCode, errorMessage);

    public static PushNotificationSendResult TransientFailure(int? httpStatusCode, string errorMessage)
        => new(PushNotificationSendStatus.TransientFailure, httpStatusCode, errorMessage);
}

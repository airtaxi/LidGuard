namespace LidGuard.Notifications.Data;

internal static class DeliveryStatuses
{
    public const string Succeeded = nameof(Succeeded);
    public const string PermanentFailure = nameof(PermanentFailure);
    public const string TransientFailure = nameof(TransientFailure);
}

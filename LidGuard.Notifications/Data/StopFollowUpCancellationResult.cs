namespace LidGuard.Notifications.Data;

internal sealed record StopFollowUpCancellationResult(
    bool Succeeded,
    string Status,
    string Message);

namespace LidGuard.Notifications.Models;

internal sealed class StopFollowUpReplyRequest
{
    public string? Reply { get; init; }

    public bool WaitForConsumption { get; init; }
}

internal sealed class StopFollowUpExtendRequest
{
    public int? ExtendMinutes { get; init; }
}

internal sealed class StopFollowUpActionResponse
{
    public bool Succeeded { get; init; }

    public bool Extended { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset? DeadlineAtUtc { get; init; }

    public DateTimeOffset? MaximumDeadlineAtUtc { get; init; }

    public int ProviderHookTimeoutRemainingSeconds { get; init; }

    public string ProviderHookTimeoutRemainingText { get; init; } = string.Empty;

    public bool ReplyConsumed { get; init; }

    public DateTimeOffset? ConsumedAtUtc { get; init; }
}

internal sealed class StopFollowUpWebhookAcceptedResponse
{
    public string FollowUpRequestIdentifier { get; init; } = string.Empty;

    public string ReplyPollUrl { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

internal sealed class StopFollowUpPollResponse
{
    public string Status { get; init; } = string.Empty;

    public string? Reply { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

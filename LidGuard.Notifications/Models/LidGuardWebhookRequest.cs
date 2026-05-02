namespace LidGuard.Notifications.Models;

internal sealed class LidGuardWebhookRequest
{
    public string? EventType { get; init; }

    public string? Reason { get; init; }

    public int? SoftLockedSessionCount { get; init; }

    public string? Provider { get; init; }

    public string? ProviderName { get; init; }

    public string? SessionIdentifier { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? LastActivityAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public string? EndReason { get; init; }

    public int? ActiveSessionCount { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? TranscriptPath { get; init; }
}

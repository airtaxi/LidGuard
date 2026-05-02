using LidGuard.Power;
using LidGuard.Sessions;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal sealed class SuspendHistoryEntry
{
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;

    public SystemSuspendMode SuspendMode { get; init; } = SystemSuspendMode.Sleep;

    public SuspendWebhookReason Reason { get; init; } = SuspendWebhookReason.Completed;

    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public string EventName { get; init; } = string.Empty;

    public string CommandName { get; init; } = string.Empty;

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public string ProviderName { get; init; } = string.Empty;

    public string SessionIdentifier { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string SessionStateReason { get; init; } = string.Empty;

    public int ActiveSessionCount { get; init; }

    public int SuspendTriggerSessionCount { get; init; }

    public int? ObservedTemperatureCelsius { get; init; }

    public int? EmergencyHibernationTemperatureCelsius { get; init; }

    public EmergencyHibernationTemperatureMode? EmergencyHibernationTemperatureMode { get; init; }
}

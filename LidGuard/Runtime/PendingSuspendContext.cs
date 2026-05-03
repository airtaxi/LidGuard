using LidGuard.Sessions;

namespace LidGuard.Runtime;

internal readonly record struct PendingSuspendContext(
    AgentProvider Provider,
    string ProviderName,
    string SessionIdentifier,
    string WorkingDirectory,
    string CommandName,
    string SessionStateReason,
    bool IsProviderSessionEnd,
    string SessionEndReason,
    DateTimeOffset? ProviderSessionEndedAt);

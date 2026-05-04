namespace LidGuard.Ipc;

internal sealed class LiveStatusHookEventLine
{
    public DateTimeOffset Timestamp { get; init; }

    public string ProviderDisplayText { get; init; } = string.Empty;

    public string Line { get; init; } = string.Empty;
}

using LidGuard.Runtime;

namespace LidGuard.Ipc;

internal sealed class LiveStatusSnapshot
{
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public LidGuardPipeResponse Response { get; init; } = LidGuardPipeResponse.Failure("LidGuard runtime is not running.", runtimeUnavailable: true);

    public LiveStatusHookEventLine[] HookEventLines { get; init; } = [];

    public LidGuardRuntimeSessionLogEntry[] RuntimeLogEntries { get; init; } = [];

    public SuspendHistoryEntry[] SuspendHistoryEntries { get; init; } = [];

    public string[] WarningMessages { get; init; } = [];

    public static LiveStatusSnapshot RuntimeUnavailable(string message = "LidGuard runtime is not running.") => new()
    {
        Response = LidGuardPipeResponse.Failure(message, runtimeUnavailable: true, messageCode: LidGuardPipeResponseMessageCodes.RuntimeNotRunning)
    };

    public static LiveStatusSnapshot Failure(string message) => new()
    {
        Response = LidGuardPipeResponse.Failure(message)
    };
}

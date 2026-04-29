using LidGuardLib.Commons.Settings;

namespace LidGuard.Ipc;

internal sealed class LidGuardPipeResponse
{
    public bool Succeeded { get; init; }

    public bool RuntimeUnavailable { get; init; }

    public string Message { get; init; } = string.Empty;

    public int ActiveSessionCount { get; init; }

    public LidGuardSessionStatus[] Sessions { get; init; } = [];

    public LidGuardSettings Settings { get; init; } = LidGuardSettings.Default;

    public static LidGuardPipeResponse Success(string message, int activeSessionCount, LidGuardSessionStatus[] sessions, LidGuardSettings settings) => new()
    {
        Succeeded = true,
        Message = message,
        ActiveSessionCount = activeSessionCount,
        Sessions = sessions,
        Settings = settings
    };

    public static LidGuardPipeResponse Failure(string message, int activeSessionCount = 0, bool runtimeUnavailable = false) => new()
    {
        Succeeded = false,
        RuntimeUnavailable = runtimeUnavailable,
        Message = message,
        ActiveSessionCount = activeSessionCount
    };
}


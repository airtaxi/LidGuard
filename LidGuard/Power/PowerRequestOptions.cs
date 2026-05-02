using System.Text.Json.Serialization;

namespace LidGuard.Power;

public sealed class PowerRequestOptions
{
    public static PowerRequestOptions Default { get; } = new();

    public bool PreventSystemSleep { get; init; } = true;

#if LIDGUARD_LINUX || LIDGUARD_MACOS
    [JsonIgnore]
    public bool PreventAwayModeSleep { get; init; }
#else
    public bool PreventAwayModeSleep { get; init; } = true;
#endif

    public bool PreventDisplaySleep { get; init; }

    public string Reason { get; init; } = "LidGuard is keeping the system awake while an agent session is running.";

    [JsonIgnore]
#if LIDGUARD_LINUX || LIDGUARD_MACOS
    public bool HasAnyRequest => PreventSystemSleep || PreventDisplaySleep;
#else
    public bool HasAnyRequest => PreventSystemSleep || PreventAwayModeSleep || PreventDisplaySleep;
#endif
}

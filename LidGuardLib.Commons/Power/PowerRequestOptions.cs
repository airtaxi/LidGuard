using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Power;

public sealed class PowerRequestOptions
{
    public static PowerRequestOptions Default { get; } = new();

    public bool PreventSystemSleep { get; init; } = true;

    public bool PreventAwayModeSleep { get; init; } = true;

    public bool PreventDisplaySleep { get; init; }

    public string Reason { get; init; } = "LidGuard is keeping the system awake while an agent session is running.";

    [JsonIgnore]
    public bool HasAnyRequest => PreventSystemSleep || PreventAwayModeSleep || PreventDisplaySleep;
}

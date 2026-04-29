using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Power;

[JsonConverter(typeof(JsonStringEnumConverter<LidSwitchState>))]
public enum LidSwitchState
{
    Unknown = 0,
    Open = 1,
    Closed = 2
}

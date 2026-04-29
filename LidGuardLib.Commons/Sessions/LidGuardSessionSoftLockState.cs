using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<LidGuardSessionSoftLockState>))]
public enum LidGuardSessionSoftLockState
{
    None = 0,
    SoftLocked = 1
}

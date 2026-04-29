using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Power;

[JsonConverter(typeof(JsonStringEnumConverter<SystemSuspendMode>))]
public enum SystemSuspendMode
{
    Sleep = 0,
    Hibernate = 1
}

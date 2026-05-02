using System.Text.Json.Serialization;

namespace LidGuard.Power;

[JsonConverter(typeof(JsonStringEnumConverter<SystemSuspendMode>))]
public enum SystemSuspendMode
{
    Sleep = 0,
    Hibernate = 1
}

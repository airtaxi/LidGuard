using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<EmergencyHibernationTemperatureMode>))]
public enum EmergencyHibernationTemperatureMode
{
    Average = 0,
    Low = 1,
    High = 2
}

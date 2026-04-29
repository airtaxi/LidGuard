using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ClosedLidPermissionRequestDecision>))]
public enum ClosedLidPermissionRequestDecision
{
    Deny,
    Allow
}

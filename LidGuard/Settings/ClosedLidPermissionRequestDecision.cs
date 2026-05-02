using System.Text.Json.Serialization;

namespace LidGuard.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ClosedLidPermissionRequestDecision>))]
public enum ClosedLidPermissionRequestDecision
{
    Deny,
    Allow
}

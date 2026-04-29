using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<HookPermissionRequestBehavior>))]
public enum HookPermissionRequestBehavior
{
    Deny,
    Allow
}

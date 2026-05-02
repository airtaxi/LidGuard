using System.Text.Json.Serialization;
using LidGuard.Power;
using LidGuard.Settings;

namespace LidGuard.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LidGuardSettings))]
[JsonSerializable(typeof(ClosedLidPermissionRequestDecision))]
[JsonSerializable(typeof(EmergencyHibernationTemperatureMode))]
[JsonSerializable(typeof(LidSwitchState))]
[JsonSerializable(typeof(PowerRequestOptions))]
[JsonSerializable(typeof(SystemSuspendMode))]
internal sealed partial class LidGuardSettingsFileJsonSerializerContext : JsonSerializerContext
{
}


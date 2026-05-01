using System.Text.Json.Serialization;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Runtime;

[JsonSerializable(typeof(SuspendHistoryEntry))]
[JsonSerializable(typeof(AgentProvider))]
[JsonSerializable(typeof(SystemSuspendMode))]
[JsonSerializable(typeof(SuspendWebhookReason))]
[JsonSerializable(typeof(EmergencyHibernationTemperatureMode))]
[JsonSerializable(typeof(EmergencyHibernationTemperatureMode?))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(string))]
internal sealed partial class SuspendHistoryJsonSerializerContext : JsonSerializerContext
{
}

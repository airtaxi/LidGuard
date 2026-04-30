using System.Text.Json.Serialization;
using LidGuard.Control;
using LidGuard.Ipc;
using LidGuard.Mcp.Models;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Mcp;

[JsonSerializable(typeof(LidGuardSettingsStatusToolResponse))]
[JsonSerializable(typeof(LidGuardSessionListToolResponse))]
[JsonSerializable(typeof(LidGuardSettingsUpdateToolResponse))]
[JsonSerializable(typeof(LidGuardSessionRemovalToolResponse))]
[JsonSerializable(typeof(LidGuardSessionCommandToolResponse))]
[JsonSerializable(typeof(LidGuardControlSnapshot))]
[JsonSerializable(typeof(LidGuardSessionStatus))]
[JsonSerializable(typeof(LidGuardSessionStatus[]))]
[JsonSerializable(typeof(LidGuardSettings))]
[JsonSerializable(typeof(AgentProvider))]
[JsonSerializable(typeof(AgentProvider?))]
[JsonSerializable(typeof(LidGuardSessionSoftLockState))]
[JsonSerializable(typeof(LidSwitchState))]
[JsonSerializable(typeof(ClosedLidPermissionRequestDecision))]
[JsonSerializable(typeof(ClosedLidPermissionRequestDecision?))]
[JsonSerializable(typeof(EmergencyHibernationTemperatureMode))]
[JsonSerializable(typeof(EmergencyHibernationTemperatureMode?))]
[JsonSerializable(typeof(PowerRequestOptions))]
[JsonSerializable(typeof(SystemSuspendMode))]
[JsonSerializable(typeof(SystemSuspendMode?))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateTimeOffset?))]
internal sealed partial class LidGuardMcpJsonSerializerContext : JsonSerializerContext
{
}

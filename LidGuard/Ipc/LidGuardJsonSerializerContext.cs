using System.Text.Json.Serialization;
using LidGuard.Hooks;
using LidGuard.Power;
using LidGuard.Runtime;
using LidGuard.Sessions;
using LidGuard.Settings;

namespace LidGuard.Ipc;

[JsonSerializable(typeof(ClaudeHookInput))]
[JsonSerializable(typeof(CodexHookInput))]
[JsonSerializable(typeof(StopHookContinuationDecisionOutput))]
[JsonSerializable(typeof(LidGuardPipeRequest))]
[JsonSerializable(typeof(LidGuardPipeResponse))]
[JsonSerializable(typeof(LiveStatusHookEventLine))]
[JsonSerializable(typeof(LiveStatusHookEventLine[]))]
[JsonSerializable(typeof(LiveStatusSnapshot))]
[JsonSerializable(typeof(LidGuardSessionStatus[]))]
[JsonSerializable(typeof(LidGuardRuntimeSessionLogEntry))]
[JsonSerializable(typeof(LidGuardRuntimeSessionLogEntry[]))]
[JsonSerializable(typeof(SuspendHistoryEntry))]
[JsonSerializable(typeof(SuspendHistoryEntry[]))]
[JsonSerializable(typeof(ClosedLidPermissionRequestDecision))]
[JsonSerializable(typeof(EmergencyHibernationTemperatureMode))]
[JsonSerializable(typeof(LidSwitchState))]
[JsonSerializable(typeof(LidGuardSettings))]
[JsonSerializable(typeof(PowerRequestOptions))]
[JsonSerializable(typeof(SystemSuspendMode))]
internal sealed partial class LidGuardJsonSerializerContext : JsonSerializerContext
{
}


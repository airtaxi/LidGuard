using System.Text.Json.Serialization;
using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Ipc;

[JsonSerializable(typeof(ClaudeHookInput))]
[JsonSerializable(typeof(CodexHookInput))]
[JsonSerializable(typeof(LidGuardPipeRequest))]
[JsonSerializable(typeof(LidGuardPipeResponse))]
[JsonSerializable(typeof(LidGuardSessionStatus[]))]
[JsonSerializable(typeof(LidGuardSettings))]
[JsonSerializable(typeof(PowerRequestOptions))]
internal sealed partial class LidGuardJsonSerializerContext : JsonSerializerContext
{
}


using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

[JsonSerializable(typeof(LidGuardRuntimeSessionLogEntry))]
internal sealed partial class LidGuardRuntimeSessionLogJsonSerializerContext : JsonSerializerContext
{
}


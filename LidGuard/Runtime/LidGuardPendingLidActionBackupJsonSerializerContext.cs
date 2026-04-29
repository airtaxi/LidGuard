using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

[JsonSerializable(typeof(LidGuardPendingLidActionBackupState))]
internal sealed partial class LidGuardPendingLidActionBackupJsonSerializerContext : JsonSerializerContext
{
}

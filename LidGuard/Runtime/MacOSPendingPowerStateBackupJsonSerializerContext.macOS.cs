using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

[JsonSerializable(typeof(MacOSPendingPowerStateBackupState))]
internal sealed partial class MacOSPendingPowerStateBackupJsonSerializerContext : JsonSerializerContext
{
}

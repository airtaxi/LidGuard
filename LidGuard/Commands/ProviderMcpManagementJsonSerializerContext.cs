using System.Text.Json.Serialization;

namespace LidGuard.Commands;

[JsonSerializable(typeof(string[]))]
internal sealed partial class ProviderMcpManagementJsonSerializerContext : JsonSerializerContext
{
}

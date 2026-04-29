using System.Text.Json;
using ModelContextProtocol;

namespace LidGuard.Mcp;

internal static class LidGuardMcpJsonUtilities
{
    public static JsonSerializerOptions CreateToolSerializerOptions()
    {
        var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        var serializerContext = new LidGuardMcpJsonSerializerContext();
        serializerOptions.TypeInfoResolverChain.Insert(0, serializerContext);
        return serializerOptions;
    }
}

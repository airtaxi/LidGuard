using System.Text.Json.Serialization;

namespace LidGuard.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
public enum AgentProvider
{
    Unknown = -1,
    Codex = 0,
    Claude = 1,
    GitHubCopilot = 2,
    OpenCode = 3,
    Custom = 4,
    Mcp = 5
}

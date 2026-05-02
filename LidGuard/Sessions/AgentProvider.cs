using System.Text.Json.Serialization;

namespace LidGuard.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
public enum AgentProvider
{
    Unknown = 0,
    Codex = 1,
    Claude = 2,
    GitHubCopilot = 3,
    Custom = 4,
    Mcp = 5
}

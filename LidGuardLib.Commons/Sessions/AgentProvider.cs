using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]
public enum AgentProvider
{
    Unknown = 0,
    Codex = 1,
    Claude = 2,
    GitHubCopilot = 3,
    Custom = 4
}

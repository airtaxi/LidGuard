using LidGuardLib.Commons.Sessions;
using LidGuardLib.Hooks;

namespace LidGuard.Commands;

internal static class ManagedProviderConfigurationRoots
{
    private const string ClaudeUserConfigurationFileName = ".claude.json";
    private const string CopilotMcpConfigurationFileName = "mcp-config.json";

    public static string ClaudeUserConfigurationFilePath => GetUserProfileFilePath(ClaudeUserConfigurationFileName);

    public static string GitHubCopilotMcpConfigurationFilePath
        => Path.Combine(
            GitHubCopilotHookInstaller.GetDefaultGitHubCopilotConfigurationDirectoryPath(),
            CopilotMcpConfigurationFileName);

    public static IReadOnlyList<string> GetMcpCandidatePaths(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.Codex =>
            [
                CodexHookInstaller.GetDefaultCodexConfigurationDirectoryPath(),
                CodexHookInstaller.GetDefaultCodexConfigurationFilePath()
            ],
            AgentProvider.Claude =>
            [
                ClaudeUserConfigurationFilePath,
                ClaudeHookInstaller.GetDefaultClaudeConfigurationDirectoryPath()
            ],
            AgentProvider.GitHubCopilot =>
            [
                GitHubCopilotMcpConfigurationFilePath,
                GitHubCopilotHookInstaller.GetDefaultGitHubCopilotConfigurationDirectoryPath()
            ],
            _ => []
        };
    }

    public static IReadOnlyList<string> GetHookCandidatePaths(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.Codex => [CodexHookInstaller.GetDefaultCodexConfigurationDirectoryPath()],
            AgentProvider.Claude => [ClaudeHookInstaller.GetDefaultClaudeConfigurationDirectoryPath()],
            AgentProvider.GitHubCopilot =>
            [
                GitHubCopilotHookInstaller.GetDefaultGitHubCopilotConfigurationDirectoryPath(),
                Path.Combine(Environment.CurrentDirectory, ".github", "hooks"),
                Path.Combine(Environment.CurrentDirectory, ".github", "copilot")
            ],
            _ => []
        };
    }

    private static string GetUserProfileFilePath(string fileName)
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfilePath, fileName);
    }
}

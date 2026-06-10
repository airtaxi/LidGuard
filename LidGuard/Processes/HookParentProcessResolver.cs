using LidGuard.Sessions;

namespace LidGuard.Processes;

internal static partial class HookParentProcessResolver
{
    private const int MaximumParentProcessDepth = 16;

    private static readonly HashSet<string> s_nodePackageManagerProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bun",
        "node",
        "npm",
        "npx"
    };

    public static bool TryResolveWatchedProcessIdentifier(AgentProvider provider, out int watchedProcessIdentifier)
    {
        watchedProcessIdentifier = 0;
        if (provider is AgentProvider.Unknown or AgentProvider.Mcp) return false;

        using var processInfoReader = CreateProcessInfoReader();
        var processIdentifier = Environment.ProcessId;
        var visitedProcessIdentifiers = new HashSet<int> { processIdentifier };
        for (var parentProcessDepth = 0; parentProcessDepth < MaximumParentProcessDepth; parentProcessDepth++)
        {
            if (!processInfoReader.TryReadProcessInfo(processIdentifier, out var processInfo)) return false;

            var parentProcessIdentifier = processInfo.ParentProcessIdentifier;
            if (parentProcessIdentifier <= 0) return false;
            if (!visitedProcessIdentifiers.Add(parentProcessIdentifier)) return false;
            if (!processInfoReader.TryReadProcessInfo(parentProcessIdentifier, out var parentProcessInfo)) return false;

            if (IsProviderOwnerProcess(provider, parentProcessInfo))
            {
                watchedProcessIdentifier = parentProcessInfo.ProcessIdentifier;
                return true;
            }

            processIdentifier = parentProcessIdentifier;
        }

        return false;
    }

    private static bool IsProviderOwnerProcess(AgentProvider provider, HookParentProcessInfo processInfo)
        => provider switch
        {
            AgentProvider.Codex => IsCodexOwnerProcess(processInfo),
            AgentProvider.Claude => IsClaudeOwnerProcess(processInfo),
            AgentProvider.GitHubCopilot => IsGitHubCopilotOwnerProcess(processInfo),
            AgentProvider.OpenCode => IsOpenCodeOwnerProcess(processInfo),
            _ => false
        };

    private static bool IsCodexOwnerProcess(HookParentProcessInfo processInfo)
    {
        if (IsProcessName(processInfo.ProcessName, "codex") && CodexCommandLineProcessClassifier.IsAppServer(processInfo.CommandLine)) return true;
        return CodexCommandLineProcessClassifier.IsCodexCliProcess(processInfo.ProcessName, processInfo.CommandLine);
    }

    private static bool IsClaudeOwnerProcess(HookParentProcessInfo processInfo)
    {
        var normalizedProcessName = NormalizeProcessName(processInfo.ProcessName);
        if (normalizedProcessName.Contains("claude", StringComparison.OrdinalIgnoreCase)) return true;
        return IsNodePackageManagerProcess(normalizedProcessName) && processInfo.CommandLine.Contains("claude", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGitHubCopilotOwnerProcess(HookParentProcessInfo processInfo)
    {
        var normalizedProcessName = NormalizeProcessName(processInfo.ProcessName);
        if (normalizedProcessName.Contains("copilot", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalizedProcessName.Equals("gh", StringComparison.OrdinalIgnoreCase) && processInfo.CommandLine.Contains("copilot", StringComparison.OrdinalIgnoreCase)) return true;

        return IsNodePackageManagerProcess(normalizedProcessName) && processInfo.CommandLine.Contains("copilot", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenCodeOwnerProcess(HookParentProcessInfo processInfo)
    {
        var normalizedProcessName = NormalizeProcessName(processInfo.ProcessName);
        if (normalizedProcessName.Contains("opencode", StringComparison.OrdinalIgnoreCase)) return true;
        return IsNodePackageManagerProcess(normalizedProcessName) && processInfo.CommandLine.Contains("opencode", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProcessName(string processName, string expectedProcessName) => NormalizeProcessName(processName).Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase);

    private static bool IsNodePackageManagerProcess(string normalizedProcessName) => s_nodePackageManagerProcessNames.Contains(normalizedProcessName);

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        var trimmedProcessName = processName.Trim();
        var fileName = Path.GetFileNameWithoutExtension(trimmedProcessName);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = trimmedProcessName;
        return fileName.Length > 1 && fileName[0] == '-' ? fileName[1..] : fileName;
    }

    private static partial HookParentProcessInfoReader CreateProcessInfoReader();

    private abstract class HookParentProcessInfoReader : IDisposable
    {
        public abstract bool TryReadProcessInfo(int processIdentifier, out HookParentProcessInfo processInfo);

        public virtual void Dispose()
        {
        }
    }

    private readonly record struct HookParentProcessInfo(int ProcessIdentifier, int ParentProcessIdentifier, string ProcessName, string CommandLine);
}

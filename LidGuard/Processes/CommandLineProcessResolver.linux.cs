using System.Diagnostics;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Sessions;

namespace LidGuard.Processes;

public sealed class CommandLineProcessResolver : ICommandLineProcessResolver
{
    private const string ProcessRootPath = "/proc";

    private static readonly HashSet<string> s_commandLineProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash",
        "claude",
        "codex",
        "copilot",
        "dash",
        "dotnet",
        "fish",
        "gh",
        "node",
        "npm",
        "npx",
        "pwsh",
        "sh",
        "zsh"
    };

    private static readonly string[] s_lidGuardUtilityCommandNames =
    [
        "claude-hook",
        "codex-hook",
        "copilot-hook",
        "mcp-server",
        "provider-mcp-server"
    ];

    public LidGuardOperationResult<CommandLineProcessCandidate> FindForWorkingDirectory(string workingDirectory, AgentProvider provider = AgentProvider.Unknown)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("A working directory is required.");

        var normalizedWorkingDirectory = NormalizeDirectory(workingDirectory);
        var candidates = new List<(CommandLineProcessCandidate Candidate, int Score)>();

        if (!Directory.Exists(ProcessRootPath)) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("/proc is not available on this system.");

        IEnumerable<string> processDirectoryPaths;
        try { processDirectoryPaths = Directory.EnumerateDirectories(ProcessRootPath).ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return LidGuardOperationResult<CommandLineProcessCandidate>.Failure($"Failed to enumerate Linux processes from /proc: {exception.Message}");
        }

        foreach (var processDirectoryPath in processDirectoryPaths)
        {
            var processDirectoryName = Path.GetFileName(processDirectoryPath);
            if (!int.TryParse(processDirectoryName, out var processIdentifier)) continue;
            if (processIdentifier == Environment.ProcessId) continue;

            var processName = ReadProcessName(processIdentifier);
            if (string.IsNullOrWhiteSpace(processName)) continue;

            TryReadCommandLine(processIdentifier, out var commandLine);
            var isCodexAppServer = provider == AgentProvider.Codex && CodexCommandLineProcessClassifier.IsAppServer(commandLine);
            var isCodexCliProcess = provider == AgentProvider.Codex && CodexCommandLineProcessClassifier.IsCodexCliProcess(processName, commandLine);
            var score = GetProcessScore(provider, processName, isCodexCliProcess, isCodexAppServer);
            if (score == 0)
            {
                if (provider == AgentProvider.Codex) continue;
                if (!s_commandLineProcessNames.Contains(processName)) continue;
            }

            if (!TryReadCurrentDirectory(processIdentifier, out var processWorkingDirectory)) continue;
            if (!DirectoryMatches(normalizedWorkingDirectory, processWorkingDirectory)) continue;
            if (IsLidGuardUtilityCommandLine(commandLine)) continue;

            var candidate = new CommandLineProcessCandidate
            {
                ProcessIdentifier = processIdentifier,
                ProcessName = processName,
                WorkingDirectory = processWorkingDirectory,
                IsAppServer = isCodexAppServer,
                Provider = provider,
                StartedAt = GetStartedAt(processIdentifier)
            };

            candidates.Add((candidate, score));
        }

        if (candidates.Count == 0) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("No command-line process was found for the working directory.");

        var selectedCandidate = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Candidate.StartedAt)
            .First()
            .Candidate;

        return LidGuardOperationResult<CommandLineProcessCandidate>.Success(selectedCandidate);
    }

    private static bool TryReadCurrentDirectory(int processIdentifier, out string workingDirectory)
    {
        workingDirectory = string.Empty;

        try
        {
            var currentDirectoryLink = new DirectoryInfo(Path.Combine(ProcessRootPath, processIdentifier.ToString(), "cwd"));
            var resolvedDirectory = currentDirectoryLink.ResolveLinkTarget(returnFinalTarget: true);
            if (resolvedDirectory is null) return false;

            workingDirectory = resolvedDirectory.FullName;
            return !string.IsNullOrWhiteSpace(workingDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    private static string ReadProcessName(int processIdentifier)
    {
        try
        {
            var processName = File.ReadAllText(Path.Combine(ProcessRootPath, processIdentifier.ToString(), "comm")).Trim();
            return string.IsNullOrWhiteSpace(processName) ? string.Empty : processName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return string.Empty; }
    }

    private static bool TryReadCommandLine(int processIdentifier, out string commandLine)
    {
        commandLine = string.Empty;

        try
        {
            var commandLineText = File.ReadAllText(Path.Combine(ProcessRootPath, processIdentifier.ToString(), "cmdline"));
            commandLine = commandLineText.Replace('\0', ' ').Trim();
            return !string.IsNullOrWhiteSpace(commandLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static DateTimeOffset GetStartedAt(int processIdentifier)
    {
        try
        {
            using var process = Process.GetProcessById(processIdentifier);
            return new DateTimeOffset(process.StartTime);
        }
        catch { return DateTimeOffset.MinValue; }
    }

    private static int GetProcessScore(AgentProvider provider, string processName, bool isCodexCliProcess, bool isCodexAppServer)
    {
        if (provider == AgentProvider.Codex)
        {
            if (isCodexAppServer) return 0;
            return isCodexCliProcess ? 200 : 0;
        }

        if (provider == AgentProvider.Claude && processName.Equals("claude", StringComparison.OrdinalIgnoreCase)) return 100;
        if (provider == AgentProvider.GitHubCopilot && processName.Contains("copilot", StringComparison.OrdinalIgnoreCase)) return 100;
        if (s_commandLineProcessNames.Contains(processName)) return 50;
        return 0;
    }

    private static bool IsLidGuardUtilityCommandLine(string commandLine)
    {
        if (!commandLine.Contains("lidguard", StringComparison.OrdinalIgnoreCase)) return false;
        return s_lidGuardUtilityCommandNames.Any(commandName => commandLine.Contains(commandName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool DirectoryMatches(string normalizedWorkingDirectory, string processWorkingDirectory)
        => string.Equals(normalizedWorkingDirectory, NormalizeDirectory(processWorkingDirectory), StringComparison.Ordinal);

    private static string NormalizeDirectory(string directory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); }
        catch { return directory ?? string.Empty; }
    }
}

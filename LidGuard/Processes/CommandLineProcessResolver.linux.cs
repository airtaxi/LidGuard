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
        "pwsh",
        "sh",
        "zsh"
    };

    private static readonly HashSet<string> s_shellHostProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash",
        "dash",
        "fish",
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

            var isShellHosted = IsShellHostedCandidate(processIdentifier, processName);
            var score = GetProcessScore(provider, processName, isShellHosted);
            if (score == 0 && !s_commandLineProcessNames.Contains(processName)) continue;

            if (!TryReadCurrentDirectory(processIdentifier, out var processWorkingDirectory)) continue;
            if (!DirectoryMatches(normalizedWorkingDirectory, processWorkingDirectory)) continue;
            if (IsLidGuardUtilityProcess(processIdentifier)) continue;

            var candidate = new CommandLineProcessCandidate
            {
                ProcessIdentifier = processIdentifier,
                ProcessName = processName,
                WorkingDirectory = processWorkingDirectory,
                IsShellHosted = isShellHosted,
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

    private static int GetProcessScore(AgentProvider provider, string processName, bool isShellHosted)
    {
        if (provider == AgentProvider.Codex)
        {
            if (processName.Equals("codex", StringComparison.OrdinalIgnoreCase)) return isShellHosted ? 200 : 100;
            if (isShellHosted) return 150;
        }

        if (provider == AgentProvider.Claude && processName.Equals("claude", StringComparison.OrdinalIgnoreCase)) return 100;
        if (provider == AgentProvider.GitHubCopilot && processName.Contains("copilot", StringComparison.OrdinalIgnoreCase)) return 100;
        if (s_commandLineProcessNames.Contains(processName)) return 50;
        return 0;
    }

    private static bool IsShellHostedCandidate(int processIdentifier, string processName)
    {
        if (s_shellHostProcessNames.Contains(processName)) return true;
        if (!TryReadParentProcessIdentifier(processIdentifier, out var parentProcessIdentifier) || parentProcessIdentifier <= 0) return false;

        var parentProcessName = ReadProcessName(parentProcessIdentifier);
        return !string.IsNullOrWhiteSpace(parentProcessName) && s_shellHostProcessNames.Contains(parentProcessName);
    }

    private static bool TryReadParentProcessIdentifier(int processIdentifier, out int parentProcessIdentifier)
    {
        parentProcessIdentifier = 0;

        try
        {
            var statText = File.ReadAllText(Path.Combine(ProcessRootPath, processIdentifier.ToString(), "stat"));
            var processNameEndIndex = statText.LastIndexOf(')');
            if (processNameEndIndex < 0 || processNameEndIndex + 2 >= statText.Length) return false;

            var statFields = statText[(processNameEndIndex + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (statFields.Length < 2) return false;

            return int.TryParse(statFields[1], out parentProcessIdentifier);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsLidGuardUtilityProcess(int processIdentifier)
    {
        if (!TryReadCommandLine(processIdentifier, out var commandLine)) return false;

        return IsLidGuardUtilityCommandLine(commandLine);
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

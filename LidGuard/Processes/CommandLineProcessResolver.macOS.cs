using System.Diagnostics;
using LidGuard.Platform;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Sessions;

namespace LidGuard.Processes;

public sealed class CommandLineProcessResolver : ICommandLineProcessResolver
{
    private static readonly TimeSpan s_processListTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_currentDirectoryTimeout = TimeSpan.FromSeconds(2);

    private static readonly HashSet<string> s_commandLineProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash",
        "claude",
        "codex",
        "copilot",
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

    public LidGuardOperationResult<CommandLineProcessCandidate> FindForWorkingDirectory(string workingDirectory, AgentProvider provider = AgentProvider.Unknown)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("A working directory is required.");
        if (!MacOSCommandPathResolver.TryFindExecutable("ps", out var processListPath)) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("ps was not found on PATH.");

        var processListResult = MacOSCommandRunner.Run(processListPath, ["-axo", "pid=,ppid=,comm=,command="], s_processListTimeout);
        if (!processListResult.Succeeded) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure(processListResult.CreateFailureMessage("ps"), processListResult.ExitCode);

        var processRows = ParseProcessRows(processListResult.StandardOutput);
        var normalizedWorkingDirectory = NormalizeDirectory(workingDirectory);
        var candidates = new List<(CommandLineProcessCandidate Candidate, int Score)>();

        foreach (var processRow in processRows)
        {
            if (processRow.ProcessIdentifier == Environment.ProcessId) continue;
            if (string.IsNullOrWhiteSpace(processRow.ProcessName)) continue;

            var isCodexAppServer = provider == AgentProvider.Codex && CodexCommandLineProcessClassifier.IsAppServer(processRow.CommandLine);
            var isCodexCliProcess = provider == AgentProvider.Codex && CodexCommandLineProcessClassifier.IsCodexCliProcess(processRow.ProcessName, processRow.CommandLine);
            var score = GetProcessScore(provider, processRow.ProcessName, isCodexCliProcess, isCodexAppServer);
            if (score == 0)
            {
                if (provider == AgentProvider.Codex) continue;
                if (!s_commandLineProcessNames.Contains(processRow.ProcessName)) continue;
            }

            if (!TryReadCurrentDirectory(processRow.ProcessIdentifier, out var processWorkingDirectory)) continue;
            if (!DirectoryMatches(normalizedWorkingDirectory, processWorkingDirectory)) continue;

            var candidate = new CommandLineProcessCandidate
            {
                ProcessIdentifier = processRow.ProcessIdentifier,
                ProcessName = processRow.ProcessName,
                WorkingDirectory = processWorkingDirectory,
                IsAppServer = isCodexAppServer,
                Provider = provider,
                StartedAt = GetStartedAt(processRow.ProcessIdentifier)
            };

            candidates.Add((candidate, score));
        }

        if (candidates.Count == 0) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("No command-line process was found for the working directory.");

        var selectedCandidate = candidates
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Candidate.StartedAt)
            .First()
            .Candidate;

        return LidGuardOperationResult<CommandLineProcessCandidate>.Success(selectedCandidate);
    }

    public static IReadOnlyList<MacOSProcessRow> ParseProcessRows(string processListOutput)
    {
        if (string.IsNullOrWhiteSpace(processListOutput)) return [];

        var processRows = new List<MacOSProcessRow>();
        foreach (var line in processListOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split([' ', '\t'], 4, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 3) continue;
            if (!int.TryParse(fields[0], out var processIdentifier)) continue;
            if (!int.TryParse(fields[1], out var parentProcessIdentifier)) continue;

            var processName = NormalizeProcessName(Path.GetFileName(fields[2]));
            if (string.IsNullOrWhiteSpace(processName)) processName = NormalizeProcessName(fields[2]);
            var commandLine = fields.Length >= 4 ? fields[3] : string.Empty;
            processRows.Add(new MacOSProcessRow(processIdentifier, parentProcessIdentifier, processName, commandLine));
        }

        return processRows;
    }

    private static bool TryReadCurrentDirectory(int processIdentifier, out string workingDirectory)
    {
        workingDirectory = string.Empty;
        if (!MacOSCommandPathResolver.TryFindExecutable("lsof", out var openFileListPath)) return false;

        var commandResult = MacOSCommandRunner.Run(
            openFileListPath,
            ["-a", "-p", processIdentifier.ToString(), "-d", "cwd", "-Fn"],
            s_currentDirectoryTimeout);
        if (!commandResult.Succeeded) return false;

        foreach (var line in commandResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('n')) continue;

            workingDirectory = line[1..];
            return !string.IsNullOrWhiteSpace(workingDirectory);
        }

        return false;
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

    private static bool DirectoryMatches(string normalizedWorkingDirectory, string processWorkingDirectory)
        => string.Equals(normalizedWorkingDirectory, NormalizeDirectory(processWorkingDirectory), StringComparison.Ordinal);

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        var normalizedProcessName = processName.Trim();
        return normalizedProcessName.Length > 1 && normalizedProcessName[0] == '-'
            ? normalizedProcessName[1..]
            : normalizedProcessName;
    }

    private static string NormalizeDirectory(string directory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); }
        catch { return directory ?? string.Empty; }
    }

    public readonly record struct MacOSProcessRow(int ProcessIdentifier, int ParentProcessIdentifier, string ProcessName, string CommandLine);
}

using LidGuard.Sessions;

namespace LidGuard.Runtime;

internal static class LidGuardWatchedProcessCleanup
{
    public static bool ShouldCleanCodexWorkingDirectory(LidGuardSessionSnapshot snapshot)
        => snapshot.Provider == AgentProvider.Codex
            && snapshot.WatchRegistrationKind == LidGuardSessionWatchRegistrationKind.CodexShellHostedWorkingDirectoryFallback
            && !string.IsNullOrWhiteSpace(snapshot.WorkingDirectory);

    public static bool WorkingDirectoriesMatch(string leftWorkingDirectory, string rightWorkingDirectory)
        => string.Equals(
            NormalizeWorkingDirectory(leftWorkingDirectory),
            NormalizeWorkingDirectory(rightWorkingDirectory),
            StringComparison.OrdinalIgnoreCase);

    public static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory ?? string.Empty; }
    }
}

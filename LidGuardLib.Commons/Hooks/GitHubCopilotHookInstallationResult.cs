namespace LidGuardLib.Commons.Hooks;

public sealed class GitHubCopilotHookInstallationResult
{
    public string BackupFilePath { get; init; } = string.Empty;

    public bool Changed { get; init; }

    public GitHubCopilotHookInstallationInspection Inspection { get; init; } = new();

    public string Message { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public static GitHubCopilotHookInstallationResult Failure(GitHubCopilotHookInstallationInspection inspection, string message)
    {
        return new GitHubCopilotHookInstallationResult
        {
            Inspection = inspection,
            Message = message,
            Succeeded = false
        };
    }

    public static GitHubCopilotHookInstallationResult Success(
        GitHubCopilotHookInstallationInspection inspection,
        bool changed,
        string message,
        string backupFilePath = "")
    {
        return new GitHubCopilotHookInstallationResult
        {
            BackupFilePath = backupFilePath,
            Changed = changed,
            Inspection = inspection,
            Message = message,
            Succeeded = true
        };
    }
}

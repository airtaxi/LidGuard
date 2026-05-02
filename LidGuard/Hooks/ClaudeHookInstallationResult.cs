namespace LidGuard.Hooks;

public sealed class ClaudeHookInstallationResult
{
    public bool Succeeded { get; init; }

    public bool Changed { get; init; }

    public ClaudeHookInstallationInspection Inspection { get; init; } = new();

    public string BackupFilePath { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static ClaudeHookInstallationResult Success(ClaudeHookInstallationInspection inspection, bool changed, string message, string backupFilePath = "")
    {
        return new ClaudeHookInstallationResult
        {
            Succeeded = true,
            Changed = changed,
            Inspection = inspection,
            BackupFilePath = backupFilePath,
            Message = message
        };
    }

    public static ClaudeHookInstallationResult Failure(ClaudeHookInstallationInspection inspection, string message)
    {
        return new ClaudeHookInstallationResult
        {
            Succeeded = false,
            Inspection = inspection,
            Message = message
        };
    }
}

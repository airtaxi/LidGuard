namespace LidGuardLib.Commons.Hooks;

public sealed class CodexHookInstallationResult
{
    public bool Succeeded { get; init; }

    public bool Changed { get; init; }

    public CodexHookInstallationInspection Inspection { get; init; } = new();

    public string BackupFilePath { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static CodexHookInstallationResult Success(CodexHookInstallationInspection inspection, bool changed, string message, string backupFilePath = "")
    {
        return new CodexHookInstallationResult
        {
            Succeeded = true,
            Changed = changed,
            Inspection = inspection,
            BackupFilePath = backupFilePath,
            Message = message
        };
    }

    public static CodexHookInstallationResult Failure(CodexHookInstallationInspection inspection, string message)
    {
        return new CodexHookInstallationResult
        {
            Succeeded = false,
            Inspection = inspection,
            Message = message
        };
    }
}

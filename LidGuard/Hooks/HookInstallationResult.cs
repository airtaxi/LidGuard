namespace LidGuard.Hooks;

public sealed class HookInstallationResult
{
    public bool Succeeded { get; init; }

    public bool Changed { get; init; }

    public HookInstallationInspection Inspection { get; init; } = new();

    public string BackupFilePath { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static HookInstallationResult Success(HookInstallationInspection inspection, bool changed, string message, string backupFilePath = "")
    {
        return new HookInstallationResult
        {
            Succeeded = true,
            Changed = changed,
            Inspection = inspection,
            BackupFilePath = backupFilePath,
            Message = message
        };
    }

    public static HookInstallationResult Failure(HookInstallationInspection inspection, string message)
    {
        return new HookInstallationResult
        {
            Succeeded = false,
            Inspection = inspection,
            Message = message
        };
    }
}

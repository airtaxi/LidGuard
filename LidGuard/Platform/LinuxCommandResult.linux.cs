namespace LidGuard.Platform;

internal sealed class LinuxCommandResult
{
    public bool Started { get; init; }

    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public bool Succeeded => Started && ExitCode == 0;

    public static LinuxCommandResult Success(int exitCode, string standardOutput, string standardError)
        => new()
        {
            Started = true,
            ExitCode = exitCode,
            StandardOutput = standardOutput ?? string.Empty,
            StandardError = standardError ?? string.Empty
        };

    public static LinuxCommandResult Failure(string message, int exitCode = -1, string standardOutput = "", string standardError = "")
        => new()
        {
            Started = false,
            ExitCode = exitCode,
            StandardOutput = standardOutput ?? string.Empty,
            StandardError = standardError ?? string.Empty,
            Message = message ?? string.Empty
        };

    public string CreateFailureMessage(string commandDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(Message)) return Message;

        var failureDetail = !string.IsNullOrWhiteSpace(StandardError)
            ? StandardError.Trim()
            : StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(failureDetail)) return $"{commandDisplayName} exited with code {ExitCode}.";
        return $"{commandDisplayName} exited with code {ExitCode}: {failureDetail}";
    }
}

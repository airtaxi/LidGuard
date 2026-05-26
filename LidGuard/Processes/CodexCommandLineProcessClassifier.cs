namespace LidGuard.Processes;

internal static class CodexCommandLineProcessClassifier
{
    private static readonly HashSet<string> s_nodePackageManagerProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node",
        "npm",
        "npx"
    };

    public static bool IsAppServer(string commandLine) => ContainsCommandLineToken(commandLine, "app-server");

    public static bool IsCodexCliProcess(string processName, string commandLine)
    {
        if (IsAppServer(commandLine)) return false;

        var normalizedProcessName = NormalizeProcessName(processName);
        if (normalizedProcessName.Equals("codex", StringComparison.Ordinal)) return true;
        if (!s_nodePackageManagerProcessNames.Contains(normalizedProcessName)) return false;
        if (string.IsNullOrWhiteSpace(commandLine)) return false;

        return commandLine.Contains("node_modules", StringComparison.OrdinalIgnoreCase) && commandLine.Contains("codex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCommandLineToken(string commandLine, string token)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;

        foreach (var commandLineToken in commandLine.Split([' ', '\t', '\r', '\n', '"'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) if (commandLineToken.Equals(token, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        var fileName = Path.GetFileNameWithoutExtension(processName.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? processName.Trim() : fileName;
    }
}

namespace LidGuardLib.Windows.Hooks;

public static class WindowsHookCommandUtilities
{
    public static string GetDefaultHookExecutableReference()
    {
        if (IsCommandAvailable("lidguard")) return "lidguard";
        return GetCurrentProcessExecutablePath();
    }

    public static string CreateHookCommand(string executablePath, string hookCommandName)
    {
        var escapedExecutableReference = EscapeHookExecutableReference(executablePath);
        return $"{escapedExecutableReference} {hookCommandName}";
    }

    public static string NormalizeHookExecutableReference(string executableReference)
    {
        if (IsPathLikeExecutableReference(executableReference)) return Path.GetFullPath(executableReference);
        return executableReference.Trim();
    }

    public static bool HookExecutableExists(string executableReference)
    {
        if (string.IsNullOrWhiteSpace(executableReference)) return false;
        if (IsPathLikeExecutableReference(executableReference)) return File.Exists(Path.GetFullPath(executableReference));
        return IsCommandAvailable(executableReference);
    }

    public static string CreateBackupFilePath(string configurationFilePath)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
        return $"{configurationFilePath}.{timestamp}.bak";
    }

    private static string GetCurrentProcessExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(processPath) ? string.Empty : Path.GetFullPath(processPath);
    }

    private static bool IsPathLikeExecutableReference(string executableReference)
    {
        if (Path.IsPathRooted(executableReference)) return true;
        return executableReference.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) || executableReference.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsCommandAvailable(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) return false;

        var candidateNames = CreateCommandCandidateNames(commandName.Trim());
        foreach (var directoryPath in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidateName in candidateNames)
            {
                try
                {
                    if (File.Exists(Path.Combine(directoryPath, candidateName))) return true;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
        }

        return false;
    }

    private static string[] CreateCommandCandidateNames(string commandName)
    {
        if (Path.HasExtension(commandName)) return [commandName];

        var executableExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(executableExtensions)) return [commandName, $"{commandName}.exe"];

        var extensions = executableExtensions.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidateNames = new List<string> { commandName };
        foreach (var extension in extensions)
        {
            var normalizedExtension = extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
            candidateNames.Add($"{commandName}{normalizedExtension}");
        }

        return [.. candidateNames];
    }

    private static string EscapeHookExecutableReference(string executableReference)
    {
        var normalizedExecutableReference = executableReference.Trim();
        if (!IsPathLikeExecutableReference(normalizedExecutableReference)) return normalizedExecutableReference;

        var escapedExecutableReference = normalizedExecutableReference.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escapedExecutableReference}\"";
    }
}

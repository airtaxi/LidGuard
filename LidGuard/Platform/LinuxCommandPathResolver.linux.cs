namespace LidGuard.Platform;

internal static class LinuxCommandPathResolver
{
    public static bool TryFindExecutable(string commandName, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(commandName)) return false;

        if (Path.IsPathRooted(commandName) || commandName.Contains(Path.DirectorySeparatorChar))
        {
            if (!File.Exists(commandName)) return false;

            executablePath = commandName;
            return true;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directoryPath in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidatePath = Path.Combine(directoryPath, commandName);
            if (!File.Exists(candidatePath)) continue;

            executablePath = candidatePath;
            return true;
        }

        return false;
    }
}

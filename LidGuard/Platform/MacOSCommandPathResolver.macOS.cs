namespace LidGuard.Platform;

internal static class MacOSCommandPathResolver
{
    private static readonly Dictionary<string, string> s_defaultExecutablePaths = new(StringComparer.Ordinal)
    {
        ["afplay"] = "/usr/bin/afplay",
        ["caffeinate"] = "/usr/bin/caffeinate",
        ["id"] = "/usr/bin/id",
        ["install"] = "/usr/bin/install",
        ["ioreg"] = "/usr/sbin/ioreg",
        ["lsof"] = "/usr/sbin/lsof",
        ["osascript"] = "/usr/bin/osascript",
        ["pmset"] = "/usr/bin/pmset",
        ["powermetrics"] = "/usr/bin/powermetrics",
        ["ps"] = "/bin/ps",
        ["rm"] = "/bin/rm",
        ["sh"] = "/bin/sh",
        ["sudo"] = "/usr/bin/sudo",
        ["system_profiler"] = "/usr/sbin/system_profiler",
        ["visudo"] = "/usr/sbin/visudo",
        ["whoami"] = "/usr/bin/whoami"
    };

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

        if (s_defaultExecutablePaths.TryGetValue(commandName, out var defaultExecutablePath) && File.Exists(defaultExecutablePath))
        {
            executablePath = defaultExecutablePath;
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

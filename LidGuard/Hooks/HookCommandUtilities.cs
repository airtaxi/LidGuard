using System.Globalization;
using LidGuard.Ipc;
using LidGuard.Processes;
using LidGuard.Sessions;
using LidGuard.Settings;

namespace LidGuard.Hooks;

public static class HookCommandUtilities
{
    public const string BashShellName = "bash";
    public const string PowerShellShellName = "powershell";

    public static string GetDefaultHookExecutableReference()
    {
        if (IsCommandAvailable("lidguard")) return "lidguard";
        return GetCurrentProcessExecutablePath();
    }

    public static string GetDefaultMcpExecutableReference()
    {
        var currentProcessExecutablePath = GetCurrentLidGuardExecutablePath();
        if (!string.IsNullOrWhiteSpace(currentProcessExecutablePath)) return currentProcessExecutablePath;

        if (TryResolveCommandExecutablePath("lidguard", out var commandExecutablePath) && commandExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return commandExecutablePath;

        if (IsCommandAvailable("lidguard")) return "lidguard";
        return GetCurrentProcessExecutablePath();
    }

    public static bool ExecutableReferencesMatch(string executableReference, string expectedExecutableReference)
    {
        if (string.IsNullOrWhiteSpace(executableReference) || string.IsNullOrWhiteSpace(expectedExecutableReference)) return false;

        if (TryResolveExecutableReferencePath(executableReference, out var executablePath) && TryResolveExecutableReferencePath(expectedExecutableReference, out var expectedExecutablePath))
        {
            return executablePath.Equals(expectedExecutablePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return executableReference.Trim().Equals(expectedExecutableReference.Trim(), comparison);
    }

    public static string CreateHookCommand(string executablePath, string hookCommandName)
    {
        var escapedExecutableReference = EscapeHookExecutableReference(executablePath);
        return $"{escapedExecutableReference} {hookCommandName}";
    }

    public static string GetCommandHookShellNameForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return PowerShellShellName;
        return BashShellName;
    }

    internal static int ResolveHookWatchedProcessIdentifier(string commandName, AgentProvider provider, LidGuardSettings settings)
    {
        if (commandName != LidGuardPipeCommands.Start) return 0;
        if (!settings.WatchParentProcess) return 0;
        return HookParentProcessResolver.TryResolveWatchedProcessIdentifier(provider, out var watchedProcessIdentifier) ? watchedProcessIdentifier : 0;
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

    public static bool TryResolveExecutableReferencePath(string executableReference, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(executableReference)) return false;

        var normalizedExecutableReference = executableReference.Trim();
        if (IsPathLikeExecutableReference(normalizedExecutableReference))
        {
            try
            {
                executablePath = Path.GetFullPath(normalizedExecutableReference);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        return TryResolveCommandExecutablePath(normalizedExecutableReference, out executablePath);
    }

    public static string CreateBackupFilePath(string configurationFilePath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return $"{configurationFilePath}.{timestamp}.bak";
    }

    private static string GetCurrentProcessExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(processPath) ? string.Empty : Path.GetFullPath(processPath);
    }

    private static string GetCurrentLidGuardExecutablePath()
    {
        var processPath = GetCurrentProcessExecutablePath();
        if (string.IsNullOrWhiteSpace(processPath)) return string.Empty;

        var fileName = Path.GetFileNameWithoutExtension(processPath);
        return fileName.Equals("lidguard", StringComparison.OrdinalIgnoreCase) ? processPath : string.Empty;
    }

    private static bool IsPathLikeExecutableReference(string executableReference)
    {
        if (Path.IsPathRooted(executableReference)) return true;
        return executableReference.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) || executableReference.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsCommandAvailable(string commandName) => TryResolveCommandExecutablePath(commandName, out _);

    private static bool TryResolveCommandExecutablePath(string commandName, out string executablePath)
    {
        executablePath = string.Empty;
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
                    var candidatePath = Path.Combine(directoryPath, candidateName);
                    if (!File.Exists(candidatePath)) continue;

                    executablePath = Path.GetFullPath(candidatePath);
                    return true;
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

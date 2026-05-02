using LidGuard.Services;

namespace LidGuard.Power;

internal sealed class LidStateSource : ILidStateSource
{
    private const string LidStateRootPath = "/proc/acpi/button/lid";

    public LidSwitchState CurrentState => ReadCurrentState();

    private static LidSwitchState ReadCurrentState()
    {
        try
        {
            if (!Directory.Exists(LidStateRootPath)) return LidSwitchState.Unknown;

            var hasOpenState = false;
            foreach (var stateFilePath in EnumerateLidStateFilePaths())
            {
                var stateText = File.ReadAllText(stateFilePath);
                if (stateText.Contains("closed", StringComparison.OrdinalIgnoreCase)) return LidSwitchState.Closed;
                if (stateText.Contains("open", StringComparison.OrdinalIgnoreCase)) hasOpenState = true;
            }

            return hasOpenState ? LidSwitchState.Open : LidSwitchState.Unknown;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return LidSwitchState.Unknown; }
    }

    private static IEnumerable<string> EnumerateLidStateFilePaths()
    {
        string[] lidDirectoryPaths;
        try { lidDirectoryPaths = Directory.EnumerateDirectories(LidStateRootPath, "*", SearchOption.TopDirectoryOnly).ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }

        var lidStateFilePaths = new List<string>();
        foreach (var lidDirectoryPath in lidDirectoryPaths)
        {
            var lidStateFilePath = Path.Combine(lidDirectoryPath, "state");
            if (File.Exists(lidStateFilePath)) lidStateFilePaths.Add(lidStateFilePath);
        }

        return lidStateFilePaths;
    }
}

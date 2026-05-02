using LidGuard.Services;

namespace LidGuard.Power;

internal sealed class VisibleDisplayMonitorCountProvider : IVisibleDisplayMonitorCountProvider
{
    private const string DisplayStatusRootPath = "/sys/class/drm";

    public int GetVisibleDisplayMonitorCount(bool excludeInternalDisplayMonitors = false)
    {
        try
        {
            if (!Directory.Exists(DisplayStatusRootPath)) return 0;

            var visibleDisplayMonitorCount = 0;
            foreach (var statusFilePath in EnumerateDisplayStatusFilePaths())
            {
                var statusText = File.ReadAllText(statusFilePath).Trim();
                if (!statusText.Equals("connected", StringComparison.OrdinalIgnoreCase)) continue;

                var connectorName = Path.GetFileName(Path.GetDirectoryName(statusFilePath)) ?? string.Empty;
                if (excludeInternalDisplayMonitors && IsInternalDisplayConnector(connectorName)) continue;

                visibleDisplayMonitorCount++;
            }

            return visibleDisplayMonitorCount;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static IEnumerable<string> EnumerateDisplayStatusFilePaths()
    {
        string[] connectorDirectoryPaths;
        try { connectorDirectoryPaths = Directory.EnumerateDirectories(DisplayStatusRootPath, "*", SearchOption.TopDirectoryOnly).ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }

        var statusFilePaths = new List<string>();
        foreach (var connectorDirectoryPath in connectorDirectoryPaths)
        {
            var statusFilePath = Path.Combine(connectorDirectoryPath, "status");
            if (File.Exists(statusFilePath)) statusFilePaths.Add(statusFilePath);
        }

        return statusFilePaths;
    }

    private static bool IsInternalDisplayConnector(string connectorName)
    {
        var normalizedConnectorName = connectorName;
        var separatorIndex = normalizedConnectorName.IndexOf('-');
        if (separatorIndex >= 0 && separatorIndex + 1 < normalizedConnectorName.Length) normalizedConnectorName = normalizedConnectorName[(separatorIndex + 1)..];

        return normalizedConnectorName.StartsWith("eDP", StringComparison.OrdinalIgnoreCase)
            || normalizedConnectorName.StartsWith("LVDS", StringComparison.OrdinalIgnoreCase)
            || normalizedConnectorName.StartsWith("DSI", StringComparison.OrdinalIgnoreCase);
    }
}

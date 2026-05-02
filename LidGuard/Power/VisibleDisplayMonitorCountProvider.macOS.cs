using System.Text.Json;
using LidGuard.Platform;
using LidGuard.Services;

namespace LidGuard.Power;

internal sealed class VisibleDisplayMonitorCountProvider : IVisibleDisplayMonitorCountProvider
{
    private static readonly TimeSpan s_systemProfilerTimeout = TimeSpan.FromSeconds(10);

    public int GetVisibleDisplayMonitorCount(bool excludeInternalDisplayMonitors = false)
    {
        if (!MacOSCommandPathResolver.TryFindExecutable("system_profiler", out var systemProfilerPath)) return 0;

        var commandResult = MacOSCommandRunner.Run(
            systemProfilerPath,
            ["SPDisplaysDataType", "-json"],
            s_systemProfilerTimeout);
        if (!commandResult.Succeeded) return 0;

        return CountVisibleDisplayMonitors(commandResult.StandardOutput, excludeInternalDisplayMonitors);
    }

    public static int CountVisibleDisplayMonitors(string systemProfilerJson, bool excludeInternalDisplayMonitors)
    {
        if (string.IsNullOrWhiteSpace(systemProfilerJson)) return 0;

        try
        {
            using var jsonDocument = JsonDocument.Parse(systemProfilerJson);
            if (!jsonDocument.RootElement.TryGetProperty("SPDisplaysDataType", out var displayDataTypeElement)) return 0;
            if (displayDataTypeElement.ValueKind != JsonValueKind.Array) return 0;

            var visibleDisplayMonitorCount = 0;
            foreach (var graphicsDeviceElement in displayDataTypeElement.EnumerateArray())
            {
                if (!graphicsDeviceElement.TryGetProperty("spdisplays_ndrvs", out var displayElements)) continue;
                if (displayElements.ValueKind != JsonValueKind.Array) continue;

                foreach (var displayElement in displayElements.EnumerateArray())
                {
                    if (displayElement.ValueKind != JsonValueKind.Object) continue;
                    if (!IsDisplayOnline(displayElement)) continue;
                    if (excludeInternalDisplayMonitors && IsInternalDisplay(displayElement)) continue;

                    visibleDisplayMonitorCount++;
                }
            }

            return visibleDisplayMonitorCount;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static bool IsDisplayOnline(JsonElement displayElement)
    {
        if (TryGetStringProperty(displayElement, "spdisplays_online", out var onlineValue) && IsNegativeValue(onlineValue)) return false;
        if (TryGetStringProperty(displayElement, "spdisplays_status", out var statusValue) && IsOfflineStatus(statusValue)) return false;
        return true;
    }

    private static bool IsInternalDisplay(JsonElement displayElement)
    {
        foreach (var property in displayElement.EnumerateObject())
        {
            var propertyName = property.Name;
            var propertyValue = GetElementText(property.Value);
            if (propertyName.Contains("builtin", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("built-in", StringComparison.OrdinalIgnoreCase)
                || propertyName.Contains("internal", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(propertyValue) || IsPositiveValue(propertyValue)) return true;
            }

            if (propertyValue.Contains("built-in", StringComparison.OrdinalIgnoreCase)
                || propertyValue.Contains("builtin", StringComparison.OrdinalIgnoreCase)
                || propertyValue.Contains("internal", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetStringProperty(JsonElement displayElement, string propertyName, out string propertyValue)
    {
        propertyValue = string.Empty;
        if (!displayElement.TryGetProperty(propertyName, out var propertyElement)) return false;

        propertyValue = GetElementText(propertyElement);
        return !string.IsNullOrWhiteSpace(propertyValue);
    }

    private static string GetElementText(JsonElement jsonElement)
        => jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => jsonElement.GetRawText(),
            _ => string.Empty
        };

    private static bool IsPositiveValue(string value)
        => value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("spdisplays_yes", StringComparison.OrdinalIgnoreCase);

    private static bool IsNegativeValue(string value)
        => value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("spdisplays_no", StringComparison.OrdinalIgnoreCase);

    private static bool IsOfflineStatus(string value)
        => value.Contains("offline", StringComparison.OrdinalIgnoreCase)
            || value.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
            || value.Contains("inactive", StringComparison.OrdinalIgnoreCase);
}

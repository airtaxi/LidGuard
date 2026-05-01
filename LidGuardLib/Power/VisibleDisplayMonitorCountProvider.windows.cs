using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Services;
using WmiLight;

namespace LidGuardLib.Power;

[SupportedOSPlatform("windows6.1")]
internal sealed partial class VisibleDisplayMonitorCountProvider : IVisibleDisplayMonitorCountProvider
{
    private const int VisibleDisplayMonitorCountSystemMetricIndex = 80;
    private const string MonitorConnectionNamespace = @"root\wmi";
    private const string MonitorConnectionQuery = "SELECT Active, VideoOutputTechnology FROM WmiMonitorConnectionParams";
    private const uint VideoOutputTechnologyLowVoltageDifferentialSwing = 6;
    private const uint VideoOutputTechnologyDisplayPortEmbedded = 11;
    private const uint VideoOutputTechnologyUnifiedDisplayInterfaceEmbedded = 13;
    private const uint VideoOutputTechnologyInternal = 0x80000000;

    public int GetVisibleDisplayMonitorCount(bool excludeInternalDisplayMonitors = false)
    {
        var desktopVisibleMonitorCount = Math.Max(0, GetSystemMetrics(VisibleDisplayMonitorCountSystemMetricIndex));
        if (desktopVisibleMonitorCount == 0) return 0;

        if (!TryGetMonitorConnectionSummary(out var monitorConnectionSummary)) return desktopVisibleMonitorCount;
        if (excludeInternalDisplayMonitors && monitorConnectionSummary.ActiveInternalMonitorCount > 0)
        {
            var activeExternalMonitorCount = Math.Max(0, monitorConnectionSummary.ActiveMonitorCount - monitorConnectionSummary.ActiveInternalMonitorCount);
            if (activeExternalMonitorCount == 0) return 0;

            return Math.Min(desktopVisibleMonitorCount, activeExternalMonitorCount);
        }

        if (monitorConnectionSummary.ActiveMonitorCount < desktopVisibleMonitorCount) return Math.Max(0, monitorConnectionSummary.ActiveMonitorCount);
        return desktopVisibleMonitorCount;
    }

    private static bool TryGetMonitorConnectionSummary(out MonitorConnectionSummary monitorConnectionSummary)
    {
        monitorConnectionSummary = default;

        try
        {
            using var connection = new WmiConnection(MonitorConnectionNamespace);
            var hasMonitorConnection = false;
            var activeMonitorCount = 0;
            var activeInternalMonitorCount = 0;

            foreach (WmiObject monitorConnection in connection.CreateQuery(MonitorConnectionQuery))
            {
                using (monitorConnection)
                {
                    if (!TryReadBooleanPropertyValue(monitorConnection, "Active", out var isActive)) continue;

                    hasMonitorConnection = true;
                    if (!isActive) continue;

                    activeMonitorCount++;
                    var videoOutputTechnology = TryReadUnsignedInt32PropertyValue(monitorConnection, "VideoOutputTechnology");
                    if (videoOutputTechnology.HasValue && IsInternalDisplayOutputTechnology(videoOutputTechnology.Value)) activeInternalMonitorCount++;
                }
            }

            if (!hasMonitorConnection) return false;

            monitorConnectionSummary = new MonitorConnectionSummary(activeMonitorCount, activeInternalMonitorCount);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool TryReadBooleanPropertyValue(WmiObject monitorConnection, string propertyName, out bool value)
    {
        value = false;

        try
        {
            var propertyValue = monitorConnection[propertyName];
            if (propertyValue is null) return false;
            if (propertyValue is bool booleanValue)
            {
                value = booleanValue;
                return true;
            }

            value = Convert.ToBoolean(propertyValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static uint? TryReadUnsignedInt32PropertyValue(WmiObject monitorConnection, string propertyName)
    {
        try
        {
            var propertyValue = monitorConnection[propertyName];
            if (propertyValue is null) return null;

            return propertyValue switch
            {
                uint unsignedIntegerValue => unsignedIntegerValue,
                int integerValue => unchecked((uint)integerValue),
                long longIntegerValue => unchecked((uint)longIntegerValue),
                ulong unsignedLongIntegerValue => unchecked((uint)unsignedLongIntegerValue),
                _ => Convert.ToUInt32(propertyValue, CultureInfo.InvariantCulture)
            };
        }
        catch (Exception) { return null; }
    }

    private static bool IsInternalDisplayOutputTechnology(uint videoOutputTechnology)
        => videoOutputTechnology is VideoOutputTechnologyLowVoltageDifferentialSwing
            or VideoOutputTechnologyDisplayPortEmbedded
            or VideoOutputTechnologyUnifiedDisplayInterfaceEmbedded
            or VideoOutputTechnologyInternal;

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static partial int GetSystemMetrics(int systemMetricIndex);

    private readonly record struct MonitorConnectionSummary(int ActiveMonitorCount, int ActiveInternalMonitorCount);
}

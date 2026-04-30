using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Services;

namespace LidGuardLib.Power;

[SupportedOSPlatform("windows6.1")]
internal sealed partial class VisibleDisplayMonitorCountProvider : IVisibleDisplayMonitorCountProvider
{
    private const int VisibleDisplayMonitorCountSystemMetricIndex = 80;

    public int GetVisibleDisplayMonitorCount() => Math.Max(0, GetSystemMetrics(VisibleDisplayMonitorCountSystemMetricIndex));

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static partial int GetSystemMetrics(int systemMetricIndex);
}

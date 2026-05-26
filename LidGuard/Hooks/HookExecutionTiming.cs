using System.Diagnostics;
using System.Globalization;
using LidGuard.Ipc;

namespace LidGuard.Hooks;

internal sealed class HookExecutionTiming
{
    private readonly Stopwatch _totalStopwatch = Stopwatch.StartNew();

    public TimeSpan InputReadDuration { get; set; }

    public TimeSpan ParseDuration { get; set; }

    public TimeSpan SettingsLoadDuration { get; set; }

    public TimeSpan ParentProcessResolveDuration { get; set; }

    public TimeSpan InterprocessCommunicationDuration { get; set; }

    public TimeSpan LogWriteDuration { get; private set; }

    public void AddLogWriteDuration(TimeSpan duration) => LogWriteDuration += duration;

    public string CreateRuntimeResultDetails(LidGuardRuntimeClientDiagnostics runtimeClientDiagnostics)
    {
        runtimeClientDiagnostics ??= new LidGuardRuntimeClientDiagnostics();
        return $"totalMs={FormatMilliseconds(_totalStopwatch.Elapsed)} "
            + $"inputReadMs={FormatMilliseconds(InputReadDuration)} "
            + $"parseMs={FormatMilliseconds(ParseDuration)} "
            + $"settingsLoadMs={FormatMilliseconds(SettingsLoadDuration)} "
            + $"parentProcessResolveMs={FormatMilliseconds(ParentProcessResolveDuration)} "
            + $"ipcMs={FormatMilliseconds(InterprocessCommunicationDuration)} "
            + $"initialConnectMs={FormatMilliseconds(runtimeClientDiagnostics.InitialConnectDuration)} "
            + $"initialConnectSucceeded={runtimeClientDiagnostics.InitialConnectSucceeded} "
            + $"runtimeStartAttempted={runtimeClientDiagnostics.RuntimeStartAttempted} "
            + $"runtimeStartMs={FormatMilliseconds(runtimeClientDiagnostics.RuntimeStartDuration)} "
            + $"runtimeStartSucceeded={runtimeClientDiagnostics.RuntimeStartSucceeded} "
            + $"startupConnectMs={FormatMilliseconds(runtimeClientDiagnostics.StartupConnectDuration)} "
            + $"startupConnectSucceeded={runtimeClientDiagnostics.StartupConnectSucceeded} "
            + $"sendReceiveMs={FormatMilliseconds(runtimeClientDiagnostics.SendReceiveDuration)} "
            + $"logWriteMs={FormatMilliseconds(LogWriteDuration)}";
    }

    private static string FormatMilliseconds(TimeSpan duration) => duration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
}

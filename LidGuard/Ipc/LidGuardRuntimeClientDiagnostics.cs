namespace LidGuard.Ipc;

internal sealed class LidGuardRuntimeClientDiagnostics
{
    public TimeSpan InitialConnectDuration { get; set; }

    public bool InitialConnectSucceeded { get; set; }

    public bool RuntimeStartAttempted { get; set; }

    public TimeSpan RuntimeStartDuration { get; set; }

    public bool RuntimeStartSucceeded { get; set; }

    public TimeSpan StartupConnectDuration { get; set; }

    public bool StartupConnectSucceeded { get; set; }

    public TimeSpan SendReceiveDuration { get; set; }
}

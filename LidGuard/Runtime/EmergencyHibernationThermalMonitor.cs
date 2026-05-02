using LidGuard.Power;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal sealed class EmergencyHibernationThermalMonitor(
    Func<EmergencyHibernationThermalMonitorState> emergencyHibernationThermalMonitorStateProvider,
    Func<EmergencyHibernationThermalThresholdReachedContext, Task> emergencyHibernationThresholdReachedAsync)
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private CancellationTokenSource _monitorCancellationTokenSource;

    public void Cancel()
    {
        CancellationTokenSource cancellationTokenSource;

        lock (_gate)
        {
            cancellationTokenSource = _monitorCancellationTokenSource;
            if (cancellationTokenSource is null) return;
            _monitorCancellationTokenSource = null;
        }

        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }

    public void EnsureStarted()
    {
        lock (_gate)
        {
            if (_monitorCancellationTokenSource is not null) return;

            _monitorCancellationTokenSource = new CancellationTokenSource();
            _ = MonitorAsync(_monitorCancellationTokenSource.Token);
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var periodicTimer = new PeriodicTimer(s_pollInterval);

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                var emergencyHibernationThermalMonitorState = emergencyHibernationThermalMonitorStateProvider();
                if (!emergencyHibernationThermalMonitorState.ProtectionApplied) continue;
                if (!emergencyHibernationThermalMonitorState.EmergencyHibernationOnHighTemperature) continue;
                if (!emergencyHibernationThermalMonitorState.ClosedLidPolicyActive) continue;
                if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1) && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) continue;

                var emergencyHibernationTemperatureCelsius = LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius(
                    emergencyHibernationThermalMonitorState.EmergencyHibernationTemperatureCelsius);
                var emergencyHibernationTemperatureMode = emergencyHibernationThermalMonitorState.TemperatureMode;
                var observedTemperatureCelsius = SystemThermalInformation.GetSystemTemperatureCelsius(emergencyHibernationTemperatureMode);
                if (!observedTemperatureCelsius.HasValue) continue;
                if (observedTemperatureCelsius.Value < emergencyHibernationTemperatureCelsius) continue;

                await NotifyEmergencyHibernationThresholdReachedAsync(
                    new EmergencyHibernationThermalThresholdReachedContext(
                        observedTemperatureCelsius.Value,
                        emergencyHibernationTemperatureCelsius,
                        emergencyHibernationTemperatureMode),
                    emergencyHibernationThresholdReachedAsync);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task NotifyEmergencyHibernationThresholdReachedAsync(
        EmergencyHibernationThermalThresholdReachedContext emergencyHibernationThermalThresholdReachedContext,
        Func<EmergencyHibernationThermalThresholdReachedContext, Task> emergencyHibernationThresholdReachedAsync)
    {
        try
        {
            await emergencyHibernationThresholdReachedAsync(emergencyHibernationThermalThresholdReachedContext);
        }
        catch
        {
        }
    }
}

internal readonly record struct EmergencyHibernationThermalMonitorState(
    bool ProtectionApplied,
    bool EmergencyHibernationOnHighTemperature,
    bool ClosedLidPolicyActive,
    LidSwitchState LidSwitchState,
    int VisibleDisplayMonitorCount,
    EmergencyHibernationTemperatureMode TemperatureMode,
    int EmergencyHibernationTemperatureCelsius);

internal readonly record struct EmergencyHibernationThermalThresholdReachedContext(
    int ObservedTemperatureCelsius,
    int ThresholdTemperatureCelsius,
    EmergencyHibernationTemperatureMode ObservedTemperatureMode);

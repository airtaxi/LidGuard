namespace LidGuard.Services;

public interface IVisibleDisplayMonitorCountProvider
{
    int GetVisibleDisplayMonitorCount(bool excludeInternalDisplayMonitors = false);
}

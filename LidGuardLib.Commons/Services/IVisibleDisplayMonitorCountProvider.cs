namespace LidGuardLib.Commons.Services;

public interface IVisibleDisplayMonitorCountProvider
{
    int GetVisibleDisplayMonitorCount(bool excludeInternalDisplayMonitors = false);
}

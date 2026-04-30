using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Settings;
using LidGuard.Ipc;

namespace LidGuard.Control;

public sealed class LidGuardControlSnapshot
{
    public string SettingsFilePath { get; init; } = string.Empty;

    public LidGuardSettings StoredSettings { get; init; } = LidGuardSettings.Default;

    public bool RuntimeReachable { get; init; }

    public bool RuntimeUnavailable { get; init; }

    public string RuntimeMessage { get; init; } = string.Empty;

    public bool HasRuntimeSettings { get; init; }

    public LidGuardSettings RuntimeSettings { get; init; } = LidGuardSettings.Default;

    public int ActiveSessionCount { get; init; }

    public LidSwitchState LidSwitchState { get; init; } = LidSwitchState.Unknown;

    public int VisibleDisplayMonitorCount { get; init; }

    public LidGuardSessionStatus[] Sessions { get; init; } = [];
}

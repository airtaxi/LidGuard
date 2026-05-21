using LidGuard.Settings;

namespace LidGuard.Hooks;

internal static class ManagedHookTimeoutConfiguration
{
    public static int GetInstalledHookTimeoutSeconds()
    {
        if (!LidGuardSettingsStore.TryLoadExistingOrDefault(out var settings, out _, out _))
            return ClosedLidStopFollowUpConfiguration.DefaultHookTimeoutSeconds;

        return ClosedLidStopFollowUpConfiguration.GetManagedHookTimeoutSeconds(settings);
    }
}

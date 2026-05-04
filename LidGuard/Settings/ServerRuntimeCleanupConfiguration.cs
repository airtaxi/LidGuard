using LidGuard.Settings;

namespace LidGuard.Settings;

internal static class ServerRuntimeCleanupConfiguration
{
    public static string GetDisplayValue(int? serverRuntimeCleanupDelayMinutes)
        => serverRuntimeCleanupDelayMinutes is null ? "off" : $"{serverRuntimeCleanupDelayMinutes.Value} minutes";

    public static bool TryValidateDelayMinutes(int? serverRuntimeCleanupDelayMinutes, out string message)
    {
        message = string.Empty;
        if (LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes(serverRuntimeCleanupDelayMinutes)) return true;

        message =
            $"Server runtime cleanup delay minutes must be off to disable automatic runtime exit or an integer of at least {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}.";
        return false;
    }
}

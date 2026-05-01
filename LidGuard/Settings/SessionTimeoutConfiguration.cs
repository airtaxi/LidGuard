using LidGuardLib.Commons.Settings;

namespace LidGuard.Settings;

internal static class SessionTimeoutConfiguration
{
    public static string GetDisplayValue(int? sessionTimeoutMinutes)
        => sessionTimeoutMinutes is null ? "off" : $"{sessionTimeoutMinutes.Value} minutes";

    public static bool TryValidateMinutes(int? sessionTimeoutMinutes, out string message)
    {
        message = string.Empty;
        if (LidGuardSettings.IsValidSessionTimeoutMinutes(sessionTimeoutMinutes)) return true;

        message = $"Session timeout minutes must be off or an integer of at least {LidGuardSettings.MinimumSessionTimeoutMinutes}.";
        return false;
    }
}

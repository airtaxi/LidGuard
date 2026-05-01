using LidGuardLib.Commons.Settings;

namespace LidGuard.Settings;

internal static class SuspendHistoryConfiguration
{
    public static string GetDisplayValue(int? suspendHistoryEntryCount)
        => suspendHistoryEntryCount is null ? "off" : $"{suspendHistoryEntryCount.Value} entries";

    public static bool TryValidateEntryCount(int? suspendHistoryEntryCount, out string message)
    {
        message = string.Empty;
        if (LidGuardSettings.IsValidSuspendHistoryEntryCount(suspendHistoryEntryCount)) return true;

        message =
            $"Suspend history count must be off or an integer of at least {LidGuardSettings.MinimumSuspendHistoryEntryCount}.";
        return false;
    }
}

using LidGuard.Localization;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class SuspendHistoryCommand
{
    public static int WriteHistory(string historyEntryCountText)
    {
        if (!LidGuardSettingsStore.TryLoadExistingOrDefault(out var storedSettings, out _, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var normalizedSettings = LidGuardSettings.Normalize(storedSettings);
        if (!TryResolveHistoryEntryCount(historyEntryCountText, normalizedSettings, out var historyEntryCount, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!SuspendHistoryLogStore.TryReadRecent(historyEntryCount, out var historyEntries, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine(LidGuardText.GetResourceString("SuspendHistoryFile", "Suspend history file: {0}").Replace("{0}", SuspendHistoryLogStore.GetDefaultLogFilePath(), StringComparison.Ordinal));
        Console.WriteLine(LidGuardText.GetResourceString("SuspendHistoryRecording", "Suspend history recording: {0}").Replace("{0}", LidGuardText.DisplaySuspendHistoryEntryCount(normalizedSettings.SuspendHistoryEntryCount), StringComparison.Ordinal));
        if (historyEntries.Length == 0)
        {
            Console.WriteLine(LidGuardText.GetResourceString("SuspendHistoryNoEntries", "No suspend history entries recorded."));
            return 0;
        }

        Console.WriteLine(LidGuardText.GetResourceString("SuspendHistoryRecentEntries", "Recent suspend history entries: {0}").Replace("{0}", historyEntries.Length.ToString(), StringComparison.Ordinal));
        foreach (var historyEntry in historyEntries) WriteHistoryEntry(historyEntry);
        return 0;
    }

    private static bool TryResolveHistoryEntryCount(
        string historyEntryCountText,
        LidGuardSettings settings,
        out int historyEntryCount,
        out string message)
    {
        historyEntryCount = settings.SuspendHistoryEntryCount ?? LidGuardSettings.DefaultSuspendHistoryEntryCount;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(historyEntryCountText)) return true;

        if (int.TryParse(historyEntryCountText.Trim(), out historyEntryCount) && historyEntryCount >= LidGuardSettings.MinimumSuspendHistoryEntryCount) return true;

        message = LidGuardText.GetResourceString("SuspendHistoryCountValidation", "The count argument must be an integer of at least {0}.")
            .Replace("{0}", LidGuardSettings.MinimumSuspendHistoryEntryCount.ToString(), StringComparison.Ordinal);
        return false;
    }

    private static void WriteHistoryEntry(SuspendHistoryEntry historyEntry)
    {
        var recordedAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(historyEntry.RecordedAt);
        Console.WriteLine(
            LidGuardText.GetResourceString(
                "SuspendHistoryEntryLine",
                "- {0} mode={1} reason={2} succeeded={3} activeSessions={4} triggerSessions={5}")
                .Replace("{0}", recordedAt, StringComparison.Ordinal)
                .Replace("{1}", LidGuardText.DisplaySuspendMode(historyEntry.SuspendMode), StringComparison.Ordinal)
                .Replace("{2}", DisplaySuspendWebhookReason(historyEntry.Reason), StringComparison.Ordinal)
                .Replace("{3}", LidGuardText.DisplayBoolean(historyEntry.Succeeded), StringComparison.Ordinal)
                .Replace("{4}", historyEntry.ActiveSessionCount.ToString(), StringComparison.Ordinal)
                .Replace("{5}", historyEntry.SuspendTriggerSessionCount.ToString(), StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(historyEntry.SessionIdentifier))
        {
            var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(historyEntry.Provider, historyEntry.ProviderName);
            Console.WriteLine(LidGuardText.GetResourceString("SuspendHistorySession", "  session={0}:{1}")
                .Replace("{0}", providerDisplayText, StringComparison.Ordinal)
                .Replace("{1}", historyEntry.SessionIdentifier, StringComparison.Ordinal));
        }

        if (historyEntry.ObservedTemperatureCelsius is not null)
        {
            var temperatureMode = historyEntry.EmergencyHibernationTemperatureMode is { } emergencyHibernationTemperatureMode
                ? LidGuardText.DisplayEmergencyHibernationTemperatureMode(emergencyHibernationTemperatureMode)
                : LidGuardText.TextDisplayNone;
            var threshold = historyEntry.EmergencyHibernationTemperatureCelsius?.ToString() ?? LidGuardText.TextDisplayNone;
            Console.WriteLine(LidGuardText.GetResourceString("SuspendHistoryTemperature", "  temperature={0} Celsius mode={1} threshold={2} Celsius")
                .Replace("{0}", historyEntry.ObservedTemperatureCelsius.Value.ToString(), StringComparison.Ordinal)
                .Replace("{1}", temperatureMode, StringComparison.Ordinal)
                .Replace("{2}", threshold, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(historyEntry.WorkingDirectory))
        {
            Console.WriteLine(LidGuardText.GetResourceString("SuspendHistoryWorkingDirectory", "  cwd=\"{0}\"")
                .Replace("{0}", historyEntry.WorkingDirectory, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(historyEntry.Message))
        {
            Console.WriteLine(LidGuardText.GetResourceString("SuspendHistoryMessage", "  message={0}")
                .Replace("{0}", historyEntry.Message, StringComparison.Ordinal));
        }
    }

    private static string DisplaySuspendWebhookReason(SuspendWebhookReason reason)
        => LidGuardText.GetResourceString($"DisplaySuspendWebhookReason{reason}", reason.ToString());
}

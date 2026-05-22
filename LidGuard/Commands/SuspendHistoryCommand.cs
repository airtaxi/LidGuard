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

        Console.WriteLine(LocalizationService.GetString("SuspendHistoryFile").Replace("{0}", SuspendHistoryLogStore.GetDefaultLogFilePath(), StringComparison.Ordinal));
        Console.WriteLine(LocalizationService.GetString("SuspendHistoryRecording").Replace("{0}", LocalizationService.DisplaySuspendHistoryEntryCount(normalizedSettings.SuspendHistoryEntryCount), StringComparison.Ordinal));
        if (historyEntries.Length == 0)
        {
            Console.WriteLine(LocalizationService.GetString("SuspendHistoryNoEntries"));
            return 0;
        }

        Console.WriteLine(LocalizationService.GetString("SuspendHistoryRecentEntries").Replace("{0}", historyEntries.Length.ToString(), StringComparison.Ordinal));
        foreach (var historyEntry in historyEntries) WriteHistoryEntry(historyEntry);
        return 0;
    }

    private static bool TryResolveHistoryEntryCount(string historyEntryCountText, LidGuardSettings settings, out int historyEntryCount, out string message)
    {
        historyEntryCount = settings.SuspendHistoryEntryCount ?? LidGuardSettings.DefaultSuspendHistoryEntryCount;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(historyEntryCountText)) return true;

        if (int.TryParse(historyEntryCountText.Trim(), out historyEntryCount) && historyEntryCount >= LidGuardSettings.MinimumSuspendHistoryEntryCount) return true;

        message = LocalizationService.GetString("SuspendHistoryCountValidation")
            .Replace("{0}", LidGuardSettings.MinimumSuspendHistoryEntryCount.ToString(), StringComparison.Ordinal);
        return false;
    }

    private static void WriteHistoryEntry(SuspendHistoryEntry historyEntry)
    {
        var recordedAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(historyEntry.RecordedAt);
        Console.WriteLine(LocalizationService.GetString("SuspendHistoryEntryLine").Replace("{0}", recordedAt, StringComparison.Ordinal).Replace("{1}", LocalizationService.DisplaySuspendMode(historyEntry.SuspendMode), StringComparison.Ordinal).Replace("{2}", DisplaySuspendWebhookReason(historyEntry.Reason), StringComparison.Ordinal).Replace("{3}", LocalizationService.DisplayBoolean(historyEntry.Succeeded), StringComparison.Ordinal).Replace("{4}", historyEntry.ActiveSessionCount.ToString(), StringComparison.Ordinal).Replace("{5}", historyEntry.SuspendTriggerSessionCount.ToString(), StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(historyEntry.SessionIdentifier))
        {
            var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(historyEntry.Provider, historyEntry.ProviderName);
            Console.WriteLine(LocalizationService.GetString("SuspendHistorySession").Replace("{0}", providerDisplayText, StringComparison.Ordinal).Replace("{1}", historyEntry.SessionIdentifier, StringComparison.Ordinal));
        }

        if (historyEntry.ObservedTemperatureCelsius is not null)
        {
            var temperatureMode = historyEntry.EmergencyHibernationTemperatureMode is { } emergencyHibernationTemperatureMode ? LocalizationService.DisplayEmergencyHibernationTemperatureMode(emergencyHibernationTemperatureMode) : LocalizationService.GetString("TextDisplayNone");
            var threshold = historyEntry.EmergencyHibernationTemperatureCelsius?.ToString() ?? LocalizationService.GetString("TextDisplayNone");
            Console.WriteLine(LocalizationService.GetString("SuspendHistoryTemperature").Replace("{0}", historyEntry.ObservedTemperatureCelsius.Value.ToString(), StringComparison.Ordinal).Replace("{1}", temperatureMode, StringComparison.Ordinal).Replace("{2}", threshold, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(historyEntry.WorkingDirectory))
        {
            Console.WriteLine(LocalizationService.GetString("SuspendHistoryWorkingDirectory").Replace("{0}", historyEntry.WorkingDirectory, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(historyEntry.Message))
        {
            Console.WriteLine(LocalizationService.GetString("SuspendHistoryMessage").Replace("{0}", historyEntry.Message, StringComparison.Ordinal));
        }
    }

    private static string DisplaySuspendWebhookReason(SuspendWebhookReason reason)
        => LocalizationService.GetString($"DisplaySuspendWebhookReason{reason}");
}

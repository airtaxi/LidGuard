using LidGuard.Ipc;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Commands;

internal static class SuspendHistoryCommand
{
    public static int WriteHistory(IReadOnlyDictionary<string, string> options)
    {
        if (!TryValidateOptions(options, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!LidGuardSettingsStore.TryLoadExistingOrDefault(out var storedSettings, out _, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var normalizedSettings = LidGuardSettings.Normalize(storedSettings);
        if (!TryResolveHistoryEntryCount(options, normalizedSettings, out var historyEntryCount, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!SuspendHistoryLogStore.TryReadRecent(historyEntryCount, out var historyEntries, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine($"Suspend history file: {SuspendHistoryLogStore.GetDefaultLogFilePath()}");
        Console.WriteLine($"Suspend history recording: {SuspendHistoryConfiguration.GetDisplayValue(normalizedSettings.SuspendHistoryEntryCount)}");
        if (historyEntries.Length == 0)
        {
            Console.WriteLine("No suspend history entries recorded.");
            return 0;
        }

        Console.WriteLine($"Recent suspend history entries: {historyEntries.Length}");
        foreach (var historyEntry in historyEntries) WriteHistoryEntry(historyEntry);
        return 0;
    }

    private static bool TryValidateOptions(IReadOnlyDictionary<string, string> options, out string message)
    {
        message = string.Empty;
        foreach (var optionName in options.Keys)
        {
            if (optionName.Equals("count", StringComparison.OrdinalIgnoreCase)) continue;

            message = $"{LidGuardPipeCommands.SuspendHistory} does not accept --{optionName}.";
            return false;
        }

        return true;
    }

    private static bool TryResolveHistoryEntryCount(
        IReadOnlyDictionary<string, string> options,
        LidGuardSettings settings,
        out int historyEntryCount,
        out string message)
    {
        historyEntryCount = settings.SuspendHistoryEntryCount ?? LidGuardSettings.DefaultSuspendHistoryEntryCount;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var historyEntryCountText, "count")) return true;

        if (int.TryParse(historyEntryCountText.Trim(), out historyEntryCount) && historyEntryCount >= LidGuardSettings.MinimumSuspendHistoryEntryCount) return true;

        message = $"The count option must be an integer of at least {LidGuardSettings.MinimumSuspendHistoryEntryCount}.";
        return false;
    }

    private static void WriteHistoryEntry(SuspendHistoryEntry historyEntry)
    {
        Console.WriteLine(
            $"- {historyEntry.RecordedAt:O} mode={historyEntry.SuspendMode} reason={historyEntry.Reason} succeeded={historyEntry.Succeeded} activeSessions={historyEntry.ActiveSessionCount} triggerSessions={historyEntry.SuspendTriggerSessionCount}");

        if (!string.IsNullOrWhiteSpace(historyEntry.SessionIdentifier))
        {
            var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(historyEntry.Provider, historyEntry.ProviderName);
            Console.WriteLine($"  session={providerDisplayText}:{historyEntry.SessionIdentifier}");
        }

        if (historyEntry.ObservedTemperatureCelsius is not null) Console.WriteLine($"  temperature={historyEntry.ObservedTemperatureCelsius} Celsius mode={historyEntry.EmergencyHibernationTemperatureMode} threshold={historyEntry.EmergencyHibernationTemperatureCelsius} Celsius");
        if (!string.IsNullOrWhiteSpace(historyEntry.WorkingDirectory)) Console.WriteLine($"  cwd=\"{historyEntry.WorkingDirectory}\"");
        if (!string.IsNullOrWhiteSpace(historyEntry.Message)) Console.WriteLine($"  message={historyEntry.Message}");
    }
}

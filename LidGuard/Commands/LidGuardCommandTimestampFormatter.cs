using System.Globalization;

namespace LidGuard.Commands;

internal static class LidGuardCommandTimestampFormatter
{
    public static string FormatDisplayTimestamp(DateTimeOffset timestamp)
        => timestamp.ToLocalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string FormatHookEventLineForDisplay(string eventLine)
    {
        var separatorIndex = eventLine.IndexOf(' ', StringComparison.Ordinal);
        var timestampText = separatorIndex < 0 ? eventLine : eventLine[..separatorIndex];
        if (!DateTimeOffset.TryParseExact(timestampText, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) return eventLine;

        var displayTimestamp = FormatDisplayTimestamp(timestamp);
        if (separatorIndex < 0) return displayTimestamp;
        return $"{displayTimestamp}{eventLine[separatorIndex..]}";
    }
}

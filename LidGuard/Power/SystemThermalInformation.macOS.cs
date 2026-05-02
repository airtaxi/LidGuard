using System.Globalization;
using LidGuard.Platform;
using LidGuard.Settings;

namespace LidGuard.Power;

public static class SystemThermalInformation
{
    private static readonly TimeSpan s_powermetricsTimeout = TimeSpan.FromSeconds(8);

    public static int? GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        var commandResult = MacOSPowerSettings.RunPrivilegedCommand(
            "powermetrics",
            ["--samplers", "smc", "-n", "1", "-i", "1000"],
            s_powermetricsTimeout);
        if (!commandResult.Succeeded) return null;

        return AggregateTemperatures(ParseCelsiusTemperatures(commandResult.StandardOutput), emergencyHibernationTemperatureMode);
    }

    public static int? AggregateTemperatures(
        IEnumerable<double> celsiusTemperatures,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        var temperatureValues = celsiusTemperatures
            .Where(static celsiusTemperature => celsiusTemperature > 0 && celsiusTemperature < 130)
            .ToArray();
        if (temperatureValues.Length == 0) return null;

        var aggregatedCelsiusTemperature = emergencyHibernationTemperatureMode switch
        {
            EmergencyHibernationTemperatureMode.Low => temperatureValues.Min(),
            EmergencyHibernationTemperatureMode.High => temperatureValues.Max(),
            _ => temperatureValues.Average()
        };
        return (int)Math.Round(aggregatedCelsiusTemperature, MidpointRounding.AwayFromZero);
    }

    public static IEnumerable<double> ParseCelsiusTemperatures(string powermetricsOutput)
    {
        if (string.IsNullOrWhiteSpace(powermetricsOutput)) return [];

        var temperatures = new List<double>();
        foreach (var line in powermetricsOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains("C", StringComparison.OrdinalIgnoreCase) && !line.Contains("celsius", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var temperature in ParseCelsiusTemperaturesFromLine(line)) temperatures.Add(temperature);
        }

        return temperatures;
    }

    private static IEnumerable<double> ParseCelsiusTemperaturesFromLine(string line)
    {
        var temperatures = new List<double>();
        for (var characterIndex = 0; characterIndex < line.Length; characterIndex++)
        {
            if (line[characterIndex] != 'C' && line[characterIndex] != 'c') continue;

            var numberEndIndex = characterIndex - 1;
            while (numberEndIndex >= 0 && (char.IsWhiteSpace(line[numberEndIndex]) || line[numberEndIndex] == '\u00b0')) numberEndIndex--;
            if (numberEndIndex < 0) continue;

            var numberStartIndex = numberEndIndex;
            while (numberStartIndex >= 0 && IsTemperatureNumberCharacter(line[numberStartIndex])) numberStartIndex--;
            numberStartIndex++;
            if (numberStartIndex > numberEndIndex) continue;

            var numberText = line[numberStartIndex..(numberEndIndex + 1)];
            if (double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)) temperatures.Add(temperature);
        }

        return temperatures;
    }

    private static bool IsTemperatureNumberCharacter(char character)
        => char.IsDigit(character) || character == '.' || character == '-';
}

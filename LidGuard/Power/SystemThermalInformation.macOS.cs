using System.Globalization;
using LidGuard.Platform;
using LidGuard.Settings;

namespace LidGuard.Power;

public static class SystemThermalInformation
{
    private const string CelsiusUnitName = "Celsius";
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
        var isInsideSystemManagementControllerSensorSection = false;
        foreach (var line in powermetricsOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsPowermetricsSectionHeader(line)) isInsideSystemManagementControllerSensorSection = IsSystemManagementControllerSensorSectionHeader(line);
            if (!CanLineContainCelsiusTemperature(line, isInsideSystemManagementControllerSensorSection)) continue;

            foreach (var temperature in ParseCelsiusTemperaturesFromLine(line)) temperatures.Add(temperature);
        }

        return temperatures;
    }

    private static IEnumerable<double> ParseCelsiusTemperaturesFromLine(string line)
    {
        var temperatures = new List<double>();
        for (var characterIndex = 0; characterIndex < line.Length; characterIndex++)
        {
            if (!IsTemperatureNumberStart(line, characterIndex)) continue;

            var numberEndIndex = ReadTemperatureNumberEndIndex(line, characterIndex, out var hasDigit);
            if (!hasDigit) continue;

            var unitStartIndex = SkipCelsiusUnitPrefix(line, numberEndIndex);
            if (!TryReadCelsiusUnit(line, unitStartIndex))
            {
                characterIndex = numberEndIndex;
                continue;
            }

            var numberText = line[characterIndex..numberEndIndex];
            if (double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)) temperatures.Add(temperature);
            characterIndex = numberEndIndex;
        }

        return temperatures;
    }

    private static bool IsPowermetricsSectionHeader(string line) => line.StartsWith("****", StringComparison.Ordinal);

    private static bool IsSystemManagementControllerSensorSectionHeader(string line)
        => line.Contains("SMC", StringComparison.OrdinalIgnoreCase)
            && line.Contains("sensor", StringComparison.OrdinalIgnoreCase);

    private static bool CanLineContainCelsiusTemperature(string line, bool isInsideSystemManagementControllerSensorSection)
        => isInsideSystemManagementControllerSensorSection
            || line.Contains("temperature", StringComparison.OrdinalIgnoreCase)
            || line.Contains("thermal", StringComparison.OrdinalIgnoreCase)
            || ContainsDelimitedToken(line, "temp");

    private static bool ContainsDelimitedToken(string line, string token)
    {
        var searchIndex = 0;
        while (searchIndex < line.Length)
        {
            var tokenIndex = line.IndexOf(token, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0) return false;

            var beforeTokenIsBoundary = tokenIndex == 0 || !char.IsLetterOrDigit(line[tokenIndex - 1]);
            var tokenEndIndex = tokenIndex + token.Length;
            var afterTokenIsBoundary = tokenEndIndex >= line.Length || !char.IsLetterOrDigit(line[tokenEndIndex]);
            if (beforeTokenIsBoundary && afterTokenIsBoundary) return true;

            searchIndex = tokenEndIndex;
        }

        return false;
    }

    private static bool IsTemperatureNumberStart(string line, int characterIndex)
    {
        var character = line[characterIndex];
        if (!char.IsDigit(character) && character is not '-' and not '+') return false;
        if (characterIndex == 0) return true;

        var previousCharacter = line[characterIndex - 1];
        return !char.IsLetterOrDigit(previousCharacter) && previousCharacter != '.';
    }

    private static int ReadTemperatureNumberEndIndex(string line, int startIndex, out bool hasDigit)
    {
        hasDigit = false;
        var currentIndex = startIndex;
        if (line[currentIndex] is '-' or '+') currentIndex++;

        while (currentIndex < line.Length && char.IsDigit(line[currentIndex]))
        {
            hasDigit = true;
            currentIndex++;
        }

        if (currentIndex < line.Length && line[currentIndex] == '.')
        {
            currentIndex++;
            while (currentIndex < line.Length && char.IsDigit(line[currentIndex]))
            {
                hasDigit = true;
                currentIndex++;
            }
        }

        return currentIndex;
    }

    private static int SkipCelsiusUnitPrefix(string line, int startIndex)
    {
        var currentIndex = startIndex;
        while (currentIndex < line.Length && char.IsWhiteSpace(line[currentIndex])) currentIndex++;
        if (currentIndex >= line.Length || line[currentIndex] != '\u00b0') return currentIndex;

        currentIndex++;
        while (currentIndex < line.Length && char.IsWhiteSpace(line[currentIndex])) currentIndex++;
        return currentIndex;
    }

    private static bool TryReadCelsiusUnit(string line, int unitStartIndex)
    {
        if (unitStartIndex >= line.Length) return false;

        if (unitStartIndex + CelsiusUnitName.Length <= line.Length
            && line[unitStartIndex..(unitStartIndex + CelsiusUnitName.Length)].Equals(CelsiusUnitName, StringComparison.OrdinalIgnoreCase))
        {
            return IsCelsiusUnitBoundary(line, unitStartIndex + CelsiusUnitName.Length);
        }

        if (line[unitStartIndex] is not 'C' and not 'c') return false;
        return IsCelsiusUnitBoundary(line, unitStartIndex + 1);
    }

    private static bool IsCelsiusUnitBoundary(string line, int unitEndIndex)
    {
        if (unitEndIndex >= line.Length) return true;

        var boundaryCharacter = line[unitEndIndex];
        return char.IsWhiteSpace(boundaryCharacter) || boundaryCharacter is ',' or ';' or ':' or '.' or ')' or ']' or '}';
    }
}

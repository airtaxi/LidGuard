using LidGuard.Settings;

namespace LidGuard.Power;

public static class SystemThermalInformation
{
    private const string ThermalZoneRootPath = "/sys/class/thermal";
    private const double MillidegreeCelsiusDivisor = 1000.0;

    public static int? GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        try
        {
            if (!Directory.Exists(ThermalZoneRootPath)) return null;

            double? lowestCelsiusTemperature = null;
            double? highestCelsiusTemperature = null;
            double celsiusTemperatureSum = 0;
            var celsiusTemperatureCount = 0;

            foreach (var temperatureFilePath in EnumerateThermalZoneTemperatureFilePaths())
            {
                var celsiusTemperature = TryReadThermalZoneTemperatureCelsius(temperatureFilePath);
                if (!celsiusTemperature.HasValue) continue;

                lowestCelsiusTemperature = !lowestCelsiusTemperature.HasValue || celsiusTemperature.Value < lowestCelsiusTemperature.Value
                    ? celsiusTemperature.Value
                    : lowestCelsiusTemperature.Value;
                highestCelsiusTemperature = !highestCelsiusTemperature.HasValue || celsiusTemperature.Value > highestCelsiusTemperature.Value
                    ? celsiusTemperature.Value
                    : highestCelsiusTemperature.Value;
                celsiusTemperatureSum += celsiusTemperature.Value;
                celsiusTemperatureCount++;
            }

            if (!lowestCelsiusTemperature.HasValue || !highestCelsiusTemperature.HasValue || celsiusTemperatureCount == 0) return null;

            var aggregatedCelsiusTemperature = emergencyHibernationTemperatureMode switch
            {
                EmergencyHibernationTemperatureMode.Low => lowestCelsiusTemperature.Value,
                EmergencyHibernationTemperatureMode.High => highestCelsiusTemperature.Value,
                _ => celsiusTemperatureSum / celsiusTemperatureCount
            };
            return (int)Math.Round(aggregatedCelsiusTemperature, MidpointRounding.AwayFromZero);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static double? TryReadThermalZoneTemperatureCelsius(string temperatureFilePath)
    {
        try
        {
            var temperatureText = File.ReadAllText(temperatureFilePath).Trim();
            if (!long.TryParse(temperatureText, out var millidegreeCelsiusTemperature)) return null;
            if (millidegreeCelsiusTemperature <= 0) return null;

            return millidegreeCelsiusTemperature / MillidegreeCelsiusDivisor;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<string> EnumerateThermalZoneTemperatureFilePaths()
    {
        string[] thermalZoneDirectoryPaths;
        try { thermalZoneDirectoryPaths = Directory.EnumerateDirectories(ThermalZoneRootPath, "thermal_zone*", SearchOption.TopDirectoryOnly).ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }

        var temperatureFilePaths = new List<string>();
        foreach (var thermalZoneDirectoryPath in thermalZoneDirectoryPaths)
        {
            var temperatureFilePath = Path.Combine(thermalZoneDirectoryPath, "temp");
            if (File.Exists(temperatureFilePath)) temperatureFilePaths.Add(temperatureFilePath);
        }

        return temperatureFilePaths;
    }
}

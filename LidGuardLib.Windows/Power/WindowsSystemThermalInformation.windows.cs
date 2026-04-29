using System.Globalization;
using System.Runtime.Versioning;
using WmiLight;

namespace LidGuardLib.Windows.Power;

[SupportedOSPlatform("windows6.1")]
public static class WindowsSystemThermalInformation
{
    private const string FormattedThermalZoneClassName = "Win32_PerfFormattedData_Counters_ThermalZoneInformation";
    private const string RawThermalZoneClassName = "Win32_PerfRawData_Counters_ThermalZoneInformation";
    private const double KelvinOffsetCelsius = 273.15;
    private const double DeciKelvinDivisor = 10.0;

    public static int? GetSystemThermalInformation()
    {
        var formattedSystemThermalInformation = TryReadHighestThermalZoneTemperatureInCelsius(FormattedThermalZoneClassName);
        if (formattedSystemThermalInformation.HasValue) return formattedSystemThermalInformation;
        return TryReadHighestThermalZoneTemperatureInCelsius(RawThermalZoneClassName);
    }

    private static int? TryReadHighestThermalZoneTemperatureInCelsius(string thermalZoneClassName)
    {
        try
        {
            using var connection = new WmiConnection();
            double? highestCelsiusTemperature = null;

            foreach (WmiObject thermalZone in connection.CreateQuery($"SELECT HighPrecisionTemperature, Temperature FROM {thermalZoneClassName}"))
            {
                using (thermalZone)
                {
                    var celsiusTemperature = TryReadThermalZoneTemperatureInCelsius(thermalZone);
                    if (!celsiusTemperature.HasValue) continue;

                    if (!highestCelsiusTemperature.HasValue || celsiusTemperature.Value > highestCelsiusTemperature.Value) highestCelsiusTemperature = celsiusTemperature.Value;
                }
            }

            if (!highestCelsiusTemperature.HasValue) return null;
            return (int)Math.Round(highestCelsiusTemperature.Value, MidpointRounding.AwayFromZero);
        }
        catch (Exception) { return null; }
    }

    private static double? TryReadThermalZoneTemperatureInCelsius(WmiObject thermalZone)
    {
        var highPrecisionTemperature = TryReadPositiveInt32PropertyValue(thermalZone, "HighPrecisionTemperature");
        if (highPrecisionTemperature.HasValue) return (highPrecisionTemperature.Value / DeciKelvinDivisor) - KelvinOffsetCelsius;

        var temperature = TryReadPositiveInt32PropertyValue(thermalZone, "Temperature");
        if (temperature.HasValue) return temperature.Value - KelvinOffsetCelsius;

        return null;
    }

    private static int? TryReadPositiveInt32PropertyValue(WmiObject thermalZone, string propertyName)
    {
        try
        {
            var propertyValue = thermalZone[propertyName];
            if (propertyValue is null) return null;

            var integerValue = Convert.ToInt32(propertyValue, CultureInfo.InvariantCulture);
            if (integerValue <= 0) return null;
            return integerValue;
        }
        catch (Exception) { return null; }
    }
}

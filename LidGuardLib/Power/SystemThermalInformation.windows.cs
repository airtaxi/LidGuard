using System.Globalization;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Settings;
using WmiLight;

namespace LidGuardLib.Power;

[SupportedOSPlatform("windows6.1")]
public static class SystemThermalInformation
{
    private const string FormattedThermalZoneClassName = "Win32_PerfFormattedData_Counters_ThermalZoneInformation";
    private const string RawThermalZoneClassName = "Win32_PerfRawData_Counters_ThermalZoneInformation";
    private const double KelvinOffsetCelsius = 273.15;
    private const double DeciKelvinDivisor = 10.0;

    public static int? GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        var formattedSystemTemperatureCelsius = TryReadSystemTemperatureCelsius(FormattedThermalZoneClassName, emergencyHibernationTemperatureMode);
        if (formattedSystemTemperatureCelsius.HasValue) return formattedSystemTemperatureCelsius;
        return TryReadSystemTemperatureCelsius(RawThermalZoneClassName, emergencyHibernationTemperatureMode);
    }

    private static int? TryReadSystemTemperatureCelsius(
        string thermalZoneClassName,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        try
        {
            using var connection = new WmiConnection();
            double? lowestCelsiusTemperature = null;
            double? highestCelsiusTemperature = null;
            double celsiusTemperatureSum = 0;
            var celsiusTemperatureCount = 0;

            foreach (WmiObject thermalZone in connection.CreateQuery($"SELECT HighPrecisionTemperature, Temperature FROM {thermalZoneClassName}"))
            {
                using (thermalZone)
                {
                    var celsiusTemperature = TryReadThermalZoneTemperatureInCelsius(thermalZone);
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
            }

            if (!lowestCelsiusTemperature.HasValue || !highestCelsiusTemperature.HasValue || celsiusTemperatureCount == 0) return null;

            var aggregatedCelsiusTemperature = GetAggregatedTemperatureCelsius(
                lowestCelsiusTemperature.Value,
                highestCelsiusTemperature.Value,
                celsiusTemperatureSum,
                celsiusTemperatureCount,
                emergencyHibernationTemperatureMode);
            return (int)Math.Round(aggregatedCelsiusTemperature, MidpointRounding.AwayFromZero);
        }
        catch (Exception) { return null; }
    }

    private static double GetAggregatedTemperatureCelsius(
        double lowestCelsiusTemperature,
        double highestCelsiusTemperature,
        double celsiusTemperatureSum,
        int celsiusTemperatureCount,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
        => emergencyHibernationTemperatureMode switch
        {
            EmergencyHibernationTemperatureMode.Low => lowestCelsiusTemperature,
            EmergencyHibernationTemperatureMode.High => highestCelsiusTemperature,
            _ => celsiusTemperatureSum / celsiusTemperatureCount
        };

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

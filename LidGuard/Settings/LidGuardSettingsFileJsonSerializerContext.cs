using System.Text.Json.Serialization;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LidGuardSettings))]
[JsonSerializable(typeof(PowerRequestOptions))]
internal sealed partial class LidGuardSettingsFileJsonSerializerContext : JsonSerializerContext
{
}


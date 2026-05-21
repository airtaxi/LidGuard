using System.Text.Json;
using System.Text.Json.Nodes;

namespace LidGuard.Hooks;

internal static class HookJsonPropertyReader
{
    public static JsonElement GetElementProperty(JsonElement jsonElement, string primaryPropertyName, string secondaryPropertyName = "")
    {
        if (jsonElement.TryGetProperty(primaryPropertyName, out var primaryPropertyElement)) return primaryPropertyElement.Clone();
        if (!string.IsNullOrWhiteSpace(secondaryPropertyName) && jsonElement.TryGetProperty(secondaryPropertyName, out var secondaryPropertyElement)) return secondaryPropertyElement.Clone();
        return default;
    }

    public static string GetStringProperty(JsonElement jsonElement, string primaryPropertyName, string secondaryPropertyName = "")
    {
        if (TryGetStringProperty(jsonElement, primaryPropertyName, out var propertyValue)) return propertyValue;
        if (!string.IsNullOrWhiteSpace(secondaryPropertyName) && TryGetStringProperty(jsonElement, secondaryPropertyName, out propertyValue)) return propertyValue;
        return string.Empty;
    }

    public static string GetStringProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : string.Empty;
    }

    public static bool? GetNullableBooleanProperty(JsonElement jsonElement, string primaryPropertyName, string secondaryPropertyName = "")
    {
        if (TryGetNullableBooleanProperty(jsonElement, primaryPropertyName, out var propertyValue)) return propertyValue;
        if (!string.IsNullOrWhiteSpace(secondaryPropertyName) && TryGetNullableBooleanProperty(jsonElement, secondaryPropertyName, out propertyValue)) return propertyValue;

        return null;
    }

    public static bool GetBooleanProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var value) && value;
    }

    private static bool TryGetNullableBooleanProperty(JsonElement jsonElement, string propertyName, out bool? value)
    {
        value = null;
        if (!jsonElement.TryGetProperty(propertyName, out var propertyValue)) return false;
        if (propertyValue.ValueKind != JsonValueKind.True && propertyValue.ValueKind != JsonValueKind.False) return false;
        value = propertyValue.GetBoolean();
        return true;
    }

    public static bool TryGetBooleanProperty(JsonElement jsonElement, string propertyName, out bool value)
    {
        value = false;
        if (jsonElement.ValueKind != JsonValueKind.Object) return false;
        if (!jsonElement.TryGetProperty(propertyName, out var propertyElement)) return false;
        if (propertyElement.ValueKind != JsonValueKind.True && propertyElement.ValueKind != JsonValueKind.False) return false;

        value = propertyElement.GetBoolean();
        return true;
    }

    public static bool TryGetNonWhiteSpaceStringProperty(JsonElement jsonElement, string propertyName, out string value)
    {
        if (!TryGetStringProperty(jsonElement, propertyName, out value)) return false;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetStringProperty(JsonElement jsonElement, string propertyName, out string value)
    {
        value = string.Empty;
        if (jsonElement.ValueKind != JsonValueKind.Object) return false;
        if (!jsonElement.TryGetProperty(propertyName, out var propertyElement)) return false;
        if (propertyElement.ValueKind != JsonValueKind.String) return false;

        value = propertyElement.GetString() ?? string.Empty;
        return true;
    }
}

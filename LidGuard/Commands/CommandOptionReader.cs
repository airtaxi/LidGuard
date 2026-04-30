namespace LidGuard.Commands;

internal static class CommandOptionReader
{
    public static bool TryParseOptions(
        string[] commandLineArguments,
        int firstOptionIndex,
        out Dictionary<string, string> options,
        out string message)
    {
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        message = string.Empty;

        for (var argumentIndex = firstOptionIndex; argumentIndex < commandLineArguments.Length; argumentIndex++)
        {
            var argument = commandLineArguments[argumentIndex];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                message = $"Unexpected argument: {argument}";
                return false;
            }

            var separatorIndex = argument.IndexOf('=');
            if (separatorIndex > 2)
            {
                var optionName = argument[2..separatorIndex];
                options[optionName] = argument[(separatorIndex + 1)..];
                continue;
            }

            var standaloneOptionName = argument[2..];
            if (string.IsNullOrWhiteSpace(standaloneOptionName))
            {
                message = "An option name is required after --.";
                return false;
            }

            if (argumentIndex + 1 >= commandLineArguments.Length || commandLineArguments[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[standaloneOptionName] = bool.TrueString;
                continue;
            }

            options[standaloneOptionName] = commandLineArguments[++argumentIndex];
        }

        return true;
    }

    public static string GetOption(IReadOnlyDictionary<string, string> options, params string[] optionNames)
        => TryGetOption(options, out var optionValue, optionNames) ? optionValue : string.Empty;

    public static bool TryGetOption(IReadOnlyDictionary<string, string> options, out string optionValue, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (options.TryGetValue(optionName, out optionValue)) return true;
        }

        optionValue = string.Empty;
        return false;
    }

    public static bool TryGetRequiredOption(
        IReadOnlyDictionary<string, string> options,
        string optionName,
        out string value,
        out string message)
    {
        value = GetOption(options, optionName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            value = value.Trim();
            message = string.Empty;
            return true;
        }

        message = $"The --{optionName} option is required.";
        return false;
    }

    public static bool TryParseBooleanOption(
        IReadOnlyDictionary<string, string> options,
        bool defaultValue,
        out bool value,
        out string message,
        params string[] optionNames)
    {
        value = defaultValue;
        message = string.Empty;
        if (!TryGetOption(options, out var valueText, optionNames)) return true;
        if (TryParseBoolean(valueText, out value)) return true;

        message = $"The {optionNames[0]} option must be true or false.";
        return false;
    }

    public static bool TryParseBoolean(string valueText, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(valueText)) return false;

        switch (valueText.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "y":
            case "on":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
            case "off":
                value = false;
                return true;
            default:
                return false;
        }
    }
}

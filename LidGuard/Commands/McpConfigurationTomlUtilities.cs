using System.Text;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class McpConfigurationTomlUtilities
{
    public const string ManagedMcpServerName = "lidguard";

    public static bool TryGetCodexMcpServerSectionContent(string configurationContent, out string sectionContent)
    {
        sectionContent = string.Empty;
        var sectionHeader = $"[mcp_servers.{ManagedMcpServerName}]";
        var lineBuilder = new StringBuilder();
        var inTargetSection = false;
        foreach (var rawLine in configurationContent.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var trimmedLine = rawLine.Trim();
            if (trimmedLine.StartsWith("[", StringComparison.Ordinal) && trimmedLine.EndsWith("]", StringComparison.Ordinal))
            {
                if (inTargetSection) break;
                if (trimmedLine.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
                {
                    inTargetSection = true;
                    continue;
                }
            }

            if (!inTargetSection) continue;
            lineBuilder.AppendLine(rawLine);
        }

        if (!inTargetSection) return false;

        sectionContent = lineBuilder.ToString();
        return true;
    }

    public static bool TryReadCodexMcpServerSection(string sectionContent, out string serverCommand, out string[] serverArgumentValues)
    {
        serverCommand = string.Empty;
        serverArgumentValues = [];

        foreach (var rawLine in sectionContent.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (!TryReadTomlAssignment(rawLine, out var key, out var value)) continue;

            if (key.Equals("command", StringComparison.Ordinal))
            {
                serverCommand = ParseTomlScalarValue(value);
                continue;
            }

            if (key.Equals("args", StringComparison.Ordinal)) serverArgumentValues = ParseTomlStringArrayValue(value);
        }

        return !string.IsNullOrWhiteSpace(serverCommand) || serverArgumentValues.Length > 0;
    }

    public static string DescribeArgumentValues(string[] serverArgumentValues)
        => serverArgumentValues.Length == 0 ? LocalizationService.GetString("TextDisplayNone") : string.Join(" | ", serverArgumentValues);

    public static bool ContainsArgument(string[] serverArgumentValues, string expectedArgument)
    {
        foreach (var serverArgumentValue in serverArgumentValues)
        {
            if (serverArgumentValue.Equals(expectedArgument, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool TryReadTomlAssignment(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var trimmedLine = line.Trim();
        var separatorIndex = trimmedLine.IndexOf('=');
        if (separatorIndex < 0) return false;

        key = trimmedLine[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;

        value = trimmedLine[(separatorIndex + 1)..].Trim();
        return true;
    }

    private static string ParseTomlScalarValue(string value)
    {
        if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
        {
            return UnescapeTomlBasicString(value[1..^1]);
        }

        if (value.Length >= 2 && value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)) return value[1..^1];
        return value;
    }

    private static string[] ParseTomlStringArrayValue(string value)
    {
        var trimmedValue = value.Trim();
        if (trimmedValue.Length < 2 || !trimmedValue.StartsWith("[", StringComparison.Ordinal) || !trimmedValue.EndsWith("]", StringComparison.Ordinal)) return [];

        var itemValues = new List<string>();
        var itemBuilder = new StringBuilder();
        var activeQuoteCharacter = '\0';
        var isEscaping = false;

        foreach (var character in trimmedValue[1..^1])
        {
            if (activeQuoteCharacter != '\0')
            {
                itemBuilder.Append(character);

                if (activeQuoteCharacter == '"' && character == '\\' && !isEscaping)
                {
                    isEscaping = true;
                    continue;
                }

                if (character == activeQuoteCharacter && !isEscaping) activeQuoteCharacter = '\0';
                else isEscaping = false;
                continue;
            }

            if (character is '"' or '\'')
            {
                activeQuoteCharacter = character;
                itemBuilder.Append(character);
                continue;
            }

            if (character == ',')
            {
                AddTomlArrayItem(itemValues, itemBuilder.ToString());
                itemBuilder.Clear();
                continue;
            }

            itemBuilder.Append(character);
        }

        AddTomlArrayItem(itemValues, itemBuilder.ToString());
        return [.. itemValues];
    }

    private static void AddTomlArrayItem(List<string> itemValues, string itemValue)
    {
        var trimmedItemValue = itemValue.Trim();
        if (trimmedItemValue.Length == 0) return;
        itemValues.Add(ParseTomlScalarValue(trimmedItemValue));
    }

    private static string UnescapeTomlBasicString(string value)
    {
        var builder = new StringBuilder();
        for (var characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            var character = value[characterIndex];
            if (character != '\\' || characterIndex + 1 >= value.Length)
            {
                builder.Append(character);
                continue;
            }

            var escapedCharacter = value[++characterIndex];
            builder.Append(escapedCharacter switch
            {
                'b' => '\b',
                't' => '\t',
                'n' => '\n',
                'f' => '\f',
                'r' => '\r',
                '"' => '"',
                '\\' => '\\',
                _ => escapedCharacter
            });
        }

        return builder.ToString();
    }
}

using System.Text;
using System.Text.Json;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public static class CodexHookConfigTomlDocument
{
    public const string ManagedBlockStartMarker = "# <LidGuard Codex hook start>";
    public const string ManagedBlockEndMarker = "# <LidGuard Codex hook end>";

    private const string FeaturesSectionHeader = "[features]";
    private const string CodexHooksFeatureKey = "codex_hooks";
    private const string StartStatusMessage = "Starting LidGuard turn protection";
    private const string PermissionRequestStatusMessage = "Responding to closed-lid permission request";
    private const string StopStatusMessage = "Stopping LidGuard session protection";
    private static readonly string[] s_requiredHookEventNames =
    [
        CodexHookEventNames.UserPromptSubmit,
        CodexHookEventNames.PermissionRequest,
        CodexHookEventNames.Stop
    ];
    private static readonly string[] s_requiredStopHookEventNames = [CodexHookEventNames.Stop];
    private static readonly string[] s_knownHookEventNames =
    [
        CodexHookEventNames.UserPromptSubmit,
        CodexHookEventNames.PermissionRequest,
        CodexHookEventNames.Stop,
        CodexHookEventNames.SessionEnd
    ];

    public static string CreateManagedHookBlock(string hookCommand)
    {
        var tomlCommandLiteral = ToTomlStringLiteral(hookCommand);
        var builder = new StringBuilder();

        builder.AppendLine(ManagedBlockStartMarker);
        AppendHookBlock(builder, CodexHookEventNames.UserPromptSubmit, tomlCommandLiteral, StartStatusMessage);
        builder.AppendLine();
        AppendHookBlock(builder, CodexHookEventNames.PermissionRequest, tomlCommandLiteral, PermissionRequestStatusMessage);
        foreach (var hookEventName in s_requiredStopHookEventNames)
        {
            builder.AppendLine();
            AppendHookBlock(builder, hookEventName, tomlCommandLiteral, StopStatusMessage);
        }

        builder.AppendLine(ManagedBlockEndMarker);

        return builder.ToString().TrimEnd();
    }

    public static CodexHookInstallationInspection InspectConfigToml(
        string configurationFilePath,
        string hookExecutablePath,
        string hookCommand,
        string content,
        bool configurationFileExists)
    {
        var hasFeatureFlag = HasCodexHooksFeatureFlag(content);
        var hasManagedBlock = HasManagedHookBlock(content);
        var contentUsedForRequiredHookInspection = hasManagedBlock ? GetManagedHookBlockContent(content) : content;
        var hasUserPromptSubmitHook = ContainsHookBlock(content, CodexHookEventNames.UserPromptSubmit);
        var hasStopHook = ContainsHookBlock(content, CodexHookEventNames.Stop);
        var hasPermissionRequestHook = ContainsHookBlock(content, CodexHookEventNames.PermissionRequest);
        var hasSessionEndHook = ContainsHookBlock(content, CodexHookEventNames.SessionEnd);
        var hasExpectedHookCommand = HasAllRequiredHookCommands(contentUsedForRequiredHookInspection, command => command.Equals(hookCommand, StringComparison.Ordinal));
        var hasValidHookCommand = HasAllRequiredHookCommands(contentUsedForRequiredHookInspection, IsLidGuardCodexHookCommand);
        var isInstalled = hasFeatureFlag && hasValidHookCommand;
        var status = isInstalled ? CodexHookInstallationStatus.Installed : hasManagedBlock ? CodexHookInstallationStatus.NeedsUpdate : CodexHookInstallationStatus.NotInstalled;
        var message = isInstalled ? "Codex hook is installed." : hasManagedBlock ? "Codex hook is installed but needs update." : "Codex hook is not installed.";

        return new CodexHookInstallationInspection
        {
            Provider = AgentProvider.Codex,
            Format = CodexHookConfigurationFormat.ConfigToml,
            Status = status,
            ConfigurationFilePath = configurationFilePath,
            HookExecutablePath = hookExecutablePath,
            HookCommand = hookCommand,
            ConfigurationFileExists = configurationFileExists,
            HasCodexHooksFeatureFlag = hasFeatureFlag,
            HasManagedBlock = hasManagedBlock,
            HasPermissionRequestHook = hasPermissionRequestHook,
            HasSessionEndHook = hasSessionEndHook,
            HasUserPromptSubmitHook = hasUserPromptSubmitHook,
            HasStopHook = hasStopHook,
            HasExpectedHookCommand = hasExpectedHookCommand,
            HasValidHookCommand = hasValidHookCommand,
            Message = message
        };
    }

    public static string InstallManagedHookBlock(string content, string hookCommand)
    {
        var updatedContent = EnsureCodexHooksFeatureFlag(content);
        var managedBlock = CreateManagedHookBlock(hookCommand);

        if (HasManagedHookBlock(updatedContent)) return ReplaceManagedHookBlock(updatedContent, managedBlock);

        if (!string.IsNullOrWhiteSpace(updatedContent) && !updatedContent.EndsWith(Environment.NewLine, StringComparison.Ordinal)) updatedContent += Environment.NewLine;
        if (!string.IsNullOrWhiteSpace(updatedContent)) updatedContent += Environment.NewLine;

        return updatedContent + managedBlock + Environment.NewLine;
    }

    public static string RemoveManagedHookBlock(string content)
    {
        if (!HasManagedHookBlock(content)) return RemoveManagedHookCommands(content);

        var startIndex = content.IndexOf(ManagedBlockStartMarker, StringComparison.Ordinal);
        var endIndex = content.IndexOf(ManagedBlockEndMarker, startIndex, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0) return content;

        endIndex += ManagedBlockEndMarker.Length;
        var before = content[..startIndex].TrimEnd();
        var after = content[endIndex..].TrimStart();
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(before)) builder.AppendLine(before);
        if (!string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(after)) builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(after)) builder.Append(after);

        var updatedContent = builder.ToString();
        if (!string.IsNullOrWhiteSpace(updatedContent) && !updatedContent.EndsWith(Environment.NewLine, StringComparison.Ordinal)) builder.AppendLine();
        return builder.ToString();
    }

    public static bool HasManagedHookBlock(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return content.Contains(ManagedBlockStartMarker, StringComparison.Ordinal) && content.Contains(ManagedBlockEndMarker, StringComparison.Ordinal);
    }

    public static bool HasCodexHooksFeatureFlag(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        var lines = SplitLines(content);
        var featuresSectionIndex = FindSectionIndex(lines, FeaturesSectionHeader);
        if (featuresSectionIndex < 0) return false;

        var nextSectionIndex = FindNextSectionIndex(lines, featuresSectionIndex + 1);
        var lastLineIndex = nextSectionIndex < 0 ? lines.Length : nextSectionIndex;
        for (var lineIndex = featuresSectionIndex + 1; lineIndex < lastLineIndex; lineIndex++)
        {
            var trimmedLine = lines[lineIndex].Trim();
            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex < 0) continue;

            var key = trimmedLine[..separatorIndex].Trim();
            if (!key.Equals(CodexHooksFeatureKey, StringComparison.Ordinal)) continue;

            var value = trimmedLine[(separatorIndex + 1)..].Trim();
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static string ToTomlStringLiteral(string value)
    {
        var escapedValue = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return $"\"{escapedValue}\"";
    }

    public static string ToJsonStringLiteral(string value) => $"\"{JsonEncodedText.Encode(value).ToString()}\"";

    private static void AppendHookBlock(StringBuilder builder, string hookEventName, string tomlCommandLiteral, string statusMessage)
    {
        builder.AppendLine($"[[hooks.{hookEventName}]]");
        builder.AppendLine($"[[hooks.{hookEventName}.hooks]]");
        builder.AppendLine("type = \"command\"");
        builder.AppendLine($"command = {tomlCommandLiteral}");
        builder.AppendLine("timeout = 30");
        builder.AppendLine($"statusMessage = \"{statusMessage}\"");
    }

    private static bool ContainsHookBlock(string content, string hookEventName) => content.Contains($"[[hooks.{hookEventName}]]", StringComparison.Ordinal);

    private static bool HasAllRequiredHookCommands(string content, Func<string, bool> commandPredicate)
    {
        foreach (var hookEventName in s_requiredHookEventNames)
        {
            if (!ContainsHookCommand(content, hookEventName, commandPredicate)) return false;
        }

        return true;
    }

    private static bool ContainsHookCommand(string content, string hookEventName, Func<string, bool> commandPredicate)
    {
        var lines = SplitLines(content);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!IsHookCommandTableHeader(lines[lineIndex], hookEventName)) continue;

            var nextTableIndex = FindNextTableIndex(lines, lineIndex + 1);
            var commandLineEndIndex = nextTableIndex < 0 ? lines.Length : nextTableIndex;
            for (var commandLineIndex = lineIndex + 1; commandLineIndex < commandLineEndIndex; commandLineIndex++)
            {
                if (TryReadCommandValue(lines[commandLineIndex], out var command) && commandPredicate(command)) return true;
            }
        }

        return false;
    }

    private static string RemoveManagedHookCommands(string content)
    {
        var lines = SplitLines(content);
        var removedLineIndexes = new HashSet<int>();

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!TryGetHookEventNameFromCommandTableHeader(lines[lineIndex], out _)) continue;

            var nextTableIndex = FindNextTableIndex(lines, lineIndex + 1);
            var hookBlockEndIndex = nextTableIndex < 0 ? lines.Length : nextTableIndex;
            if (!HookBlockContainsCommand(lines, lineIndex + 1, hookBlockEndIndex, IsLidGuardCodexHookCommand)) continue;

            for (var removeLineIndex = lineIndex; removeLineIndex < hookBlockEndIndex; removeLineIndex++) removedLineIndexes.Add(removeLineIndex);
        }

        if (removedLineIndexes.Count == 0) return content;

        var remainingLines = new List<string>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!removedLineIndexes.Contains(lineIndex)) remainingLines.Add(lines[lineIndex]);
        }

        RemoveEmptyHookMatcherTables(remainingLines);
        var updatedContent = JoinLines([.. remainingLines]).TrimEnd();
        return string.IsNullOrWhiteSpace(updatedContent) ? string.Empty : updatedContent + Environment.NewLine;
    }

    private static bool HookBlockContainsCommand(string[] lines, int startIndex, int endIndex, Func<string, bool> commandPredicate)
    {
        for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++)
        {
            if (TryReadCommandValue(lines[lineIndex], out var command) && commandPredicate(command)) return true;
        }

        return false;
    }

    private static void RemoveEmptyHookMatcherTables(List<string> lines)
    {
        for (var lineIndex = lines.Count - 1; lineIndex >= 0; lineIndex--)
        {
            if (!TryGetHookEventNameFromMatcherTableHeader(lines[lineIndex], out var hookEventName)) continue;

            var nextTableIndex = FindNextTableIndex(lines, lineIndex + 1);
            var hookMatcherEndIndex = nextTableIndex < 0 ? lines.Count : nextTableIndex;
            if (ContainsMeaningfulContent(lines, lineIndex + 1, hookMatcherEndIndex)) continue;
            if (nextTableIndex >= 0 && IsHookCommandTableHeader(lines[nextTableIndex], hookEventName)) continue;

            lines.RemoveRange(lineIndex, hookMatcherEndIndex - lineIndex);
        }
    }

    private static bool ContainsMeaningfulContent(IReadOnlyList<string> lines, int startIndex, int endIndex)
    {
        for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++)
        {
            var trimmedLine = lines[lineIndex].Trim();
            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("#", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool IsHookCommandTableHeader(string line, string hookEventName) =>
        line.Trim().Equals($"[[hooks.{hookEventName}.hooks]]", StringComparison.Ordinal);

    private static bool TryGetHookEventNameFromCommandTableHeader(string line, out string hookEventName)
    {
        hookEventName = string.Empty;
        var trimmedLine = line.Trim();
        const string prefix = "[[hooks.";
        const string suffix = ".hooks]]";
        if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal) || !trimmedLine.EndsWith(suffix, StringComparison.Ordinal)) return false;

        var candidateHookEventName = trimmedLine[prefix.Length..^suffix.Length];
        if (!IsKnownHookEventName(candidateHookEventName)) return false;

        hookEventName = candidateHookEventName;
        return true;
    }

    private static bool TryGetHookEventNameFromMatcherTableHeader(string line, out string hookEventName)
    {
        hookEventName = string.Empty;
        var trimmedLine = line.Trim();
        const string prefix = "[[hooks.";
        const string suffix = "]]";
        if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal) || !trimmedLine.EndsWith(suffix, StringComparison.Ordinal)) return false;
        if (trimmedLine.EndsWith(".hooks]]", StringComparison.Ordinal)) return false;

        var candidateHookEventName = trimmedLine[prefix.Length..^suffix.Length];
        if (!IsKnownHookEventName(candidateHookEventName)) return false;

        hookEventName = candidateHookEventName;
        return true;
    }

    private static bool TryReadCommandValue(string line, out string command)
    {
        command = string.Empty;
        var trimmedLine = line.Trim();
        var separatorIndex = trimmedLine.IndexOf('=');
        if (separatorIndex < 0) return false;

        var key = trimmedLine[..separatorIndex].Trim();
        if (!key.Equals("command", StringComparison.Ordinal)) return false;

        var value = trimmedLine[(separatorIndex + 1)..].Trim();
        command = ParseTomlStringValue(value);
        return true;
    }

    private static string ParseTomlStringValue(string value)
    {
        if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
        {
            return UnescapeTomlBasicString(value[1..^1]);
        }

        if (value.Length >= 2 && value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)) return value[1..^1];
        return value;
    }

    private static bool IsKnownHookEventName(string hookEventName)
    {
        foreach (var knownHookEventName in s_knownHookEventNames)
        {
            if (knownHookEventName.Equals(hookEventName, StringComparison.Ordinal)) return true;
        }

        return false;
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

    private static bool IsLidGuardCodexHookCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return command.Contains("lidguard", StringComparison.OrdinalIgnoreCase) && command.Contains("codex-hook", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetManagedHookBlockContent(string content)
    {
        var startIndex = content.IndexOf(ManagedBlockStartMarker, StringComparison.Ordinal);
        var endIndex = content.IndexOf(ManagedBlockEndMarker, startIndex, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0) return content;

        endIndex += ManagedBlockEndMarker.Length;
        return content[startIndex..endIndex];
    }

    private static string EnsureCodexHooksFeatureFlag(string content)
    {
        var lines = SplitLines(content);
        var featuresSectionIndex = FindSectionIndex(lines, FeaturesSectionHeader);
        if (featuresSectionIndex < 0)
        {
            var prefix = $"{FeaturesSectionHeader}{Environment.NewLine}{CodexHooksFeatureKey} = true{Environment.NewLine}";
            if (string.IsNullOrWhiteSpace(content)) return prefix;
            return prefix + Environment.NewLine + content.TrimStart();
        }

        var nextSectionIndex = FindNextSectionIndex(lines, featuresSectionIndex + 1);
        var lastLineIndex = nextSectionIndex < 0 ? lines.Length : nextSectionIndex;
        for (var lineIndex = featuresSectionIndex + 1; lineIndex < lastLineIndex; lineIndex++)
        {
            var trimmedLine = lines[lineIndex].Trim();
            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex < 0) continue;

            var key = trimmedLine[..separatorIndex].Trim();
            if (!key.Equals(CodexHooksFeatureKey, StringComparison.Ordinal)) continue;

            lines[lineIndex] = $"{CodexHooksFeatureKey} = true";
            return JoinLines(lines);
        }

        var updatedLines = new List<string>(lines);
        updatedLines.Insert(featuresSectionIndex + 1, $"{CodexHooksFeatureKey} = true");
        return JoinLines([.. updatedLines]);
    }

    private static string ReplaceManagedHookBlock(string content, string managedBlock)
    {
        var startIndex = content.IndexOf(ManagedBlockStartMarker, StringComparison.Ordinal);
        var endIndex = content.IndexOf(ManagedBlockEndMarker, startIndex, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0) return content;

        endIndex += ManagedBlockEndMarker.Length;
        var before = content[..startIndex].TrimEnd();
        var after = content[endIndex..].TrimStart();
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(before))
        {
            builder.AppendLine(before);
            builder.AppendLine();
        }

        builder.AppendLine(managedBlock);

        if (!string.IsNullOrWhiteSpace(after))
        {
            builder.AppendLine();
            builder.Append(after);
        }

        if (!builder.ToString().EndsWith(Environment.NewLine, StringComparison.Ordinal)) builder.AppendLine();
        return builder.ToString();
    }

    private static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return [];
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    private static string JoinLines(string[] lines) => string.Join(Environment.NewLine, lines);

    private static int FindSectionIndex(string[] lines, string sectionHeader)
    {
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lines[lineIndex].Trim().Equals(sectionHeader, StringComparison.Ordinal)) return lineIndex;
        }

        return -1;
    }

    private static int FindNextSectionIndex(string[] lines, int startIndex)
    {
        for (var lineIndex = startIndex; lineIndex < lines.Length; lineIndex++)
        {
            var trimmedLine = lines[lineIndex].Trim();
            if (trimmedLine.StartsWith("[", StringComparison.Ordinal) && trimmedLine.EndsWith("]", StringComparison.Ordinal)) return lineIndex;
        }

        return -1;
    }

    private static int FindNextTableIndex(IReadOnlyList<string> lines, int startIndex)
    {
        for (var lineIndex = startIndex; lineIndex < lines.Count; lineIndex++)
        {
            var trimmedLine = lines[lineIndex].Trim();
            if (trimmedLine.StartsWith("[", StringComparison.Ordinal) && trimmedLine.EndsWith("]", StringComparison.Ordinal)) return lineIndex;
        }

        return -1;
    }
}

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public static class CodexHookConfigTomlDocument
{
    public const string ManagedBlockStartMarker = "# <LidGuard Codex hook start>";
    public const string ManagedBlockEndMarker = "# <LidGuard Codex hook end>";

    private const string FeaturesSectionHeader = "[features]";
    private const string CodexHooksFeatureKey = "codex_hooks";
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
    private static readonly JavaScriptEncoder s_jsonEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    public static string CreateManagedHookBlock(string hookCommand)
    {
        var tomlCommandLiteral = ToTomlStringLiteral(hookCommand);
        var builder = new StringBuilder();

        builder.AppendLine(ManagedBlockStartMarker);
        AppendHookBlock(builder, CodexHookEventNames.UserPromptSubmit, tomlCommandLiteral, LidGuardText.HookStatusMessageStartingTurnProtection);
        builder.AppendLine();
        AppendHookBlock(builder, CodexHookEventNames.PermissionRequest, tomlCommandLiteral, LidGuardText.HookStatusMessageRespondingToClosedLidPermissionRequest);
        foreach (var hookEventName in s_requiredStopHookEventNames)
        {
            builder.AppendLine();
            AppendHookBlock(builder, hookEventName, tomlCommandLiteral, LidGuardText.HookStatusMessageStoppingSessionProtection);
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
        var hasManagedHookEntries = HasAnyLidGuardCodexHookCommand(content);
        var contentUsedForRequiredHookInspection = content;
        var hasUserPromptSubmitHook = ContainsHookBlock(content, CodexHookEventNames.UserPromptSubmit);
        var hasStopHook = ContainsHookBlock(content, CodexHookEventNames.Stop);
        var hasPermissionRequestHook = ContainsHookBlock(content, CodexHookEventNames.PermissionRequest);
        var hasSessionEndHook = ContainsHookBlock(content, CodexHookEventNames.SessionEnd);
        var hasExpectedHookCommand = HasAllRequiredHookCommands(contentUsedForRequiredHookInspection, command => command.Equals(hookCommand, StringComparison.Ordinal));
        var hasValidHookCommand = HasAllRequiredHookCommands(contentUsedForRequiredHookInspection, IsLidGuardCodexHookCommand);
        var isInstalled = hasFeatureFlag && hasValidHookCommand;
        var status = isInstalled ? CodexHookInstallationStatus.Installed : hasManagedHookEntries ? CodexHookInstallationStatus.NeedsUpdate : CodexHookInstallationStatus.NotInstalled;
        var message = isInstalled ? "Codex hook is installed." : hasManagedHookEntries ? "Codex hook is installed but needs update." : "Codex hook is not installed.";

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
            HasManagedHookEntries = hasManagedHookEntries,
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
        if (HasAnyLidGuardCodexHookCommand(updatedContent))
        {
            updatedContent = UpsertManagedHookCommand(updatedContent, CodexHookEventNames.UserPromptSubmit, hookCommand, LidGuardText.HookStatusMessageStartingTurnProtection);
            updatedContent = UpsertManagedHookCommand(updatedContent, CodexHookEventNames.PermissionRequest, hookCommand, LidGuardText.HookStatusMessageRespondingToClosedLidPermissionRequest);
            foreach (var hookEventName in s_requiredStopHookEventNames) updatedContent = UpsertManagedHookCommand(updatedContent, hookEventName, hookCommand, LidGuardText.HookStatusMessageStoppingSessionProtection);
            return updatedContent;
        }

        if (!string.IsNullOrWhiteSpace(updatedContent) && !updatedContent.EndsWith(Environment.NewLine, StringComparison.Ordinal)) updatedContent += Environment.NewLine;
        if (!string.IsNullOrWhiteSpace(updatedContent)) updatedContent += Environment.NewLine;

        var managedBlock = CreateManagedHookBlock(hookCommand);
        return updatedContent + managedBlock + Environment.NewLine;
    }

    public static string RemoveManagedHookBlock(string content)
    {
        var updatedContent = RemoveManagedHookCommands(content);
        return RemoveManagedBlockMarkerLines(updatedContent);
    }

    public static bool TryRefreshManagedHookStatusMessages(string content, out string updatedContent, out bool changed, out string message)
    {
        updatedContent = content;
        changed = false;
        message = string.Empty;

        var lines = new List<string>(SplitLines(content));
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (!TryGetHookEventNameFromCommandTableHeader(lines[lineIndex], out var hookEventName)) continue;
            if (!TryGetManagedHookStatusMessage(hookEventName, out var statusMessage)) continue;

            var nextTableIndex = FindNextTableIndex(lines, lineIndex + 1);
            var hookBlockEndIndex = nextTableIndex < 0 ? lines.Count : nextTableIndex;
            if (!HookBlockContainsCommand(lines, lineIndex + 1, hookBlockEndIndex, IsLidGuardCodexHookCommand)) continue;

            changed |= UpsertStatusMessageLine(lines, lineIndex + 1, hookBlockEndIndex, statusMessage);
        }

        if (!changed) return true;

        var refreshedContent = JoinLines([.. lines]).TrimEnd();
        updatedContent = string.IsNullOrWhiteSpace(refreshedContent) ? string.Empty : refreshedContent + Environment.NewLine;
        return true;
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

    public static string ToJsonStringLiteral(string value) => $"\"{JsonEncodedText.Encode(value, s_jsonEncoder).ToString()}\"";

    private static void AppendHookBlock(StringBuilder builder, string hookEventName, string tomlCommandLiteral, string statusMessage)
    {
        foreach (var line in CreateHookBlockLines(hookEventName, tomlCommandLiteral, statusMessage)) builder.AppendLine(line);
    }

    private static bool ContainsHookBlock(string content, string hookEventName) => content.Contains($"[[hooks.{hookEventName}]]", StringComparison.Ordinal);

    private static string[] CreateHookBlockLines(string hookEventName, string tomlCommandLiteral, string statusMessage)
        =>
        [
            $"[[hooks.{hookEventName}]]",
            .. CreateHookCommandTableLines(hookEventName, tomlCommandLiteral, statusMessage)
        ];

    private static string[] CreateHookCommandTableLines(string hookEventName, string tomlCommandLiteral, string statusMessage)
        =>
        [
            $"[[hooks.{hookEventName}.hooks]]",
            "type = \"command\"",
            $"command = {tomlCommandLiteral}",
            "timeout = 30",
            $"statusMessage = {ToTomlStringLiteral(statusMessage)}"
        ];

    private static bool HasAnyLidGuardCodexHookCommand(string content)
    {
        var lines = SplitLines(content);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!TryGetHookEventNameFromCommandTableHeader(lines[lineIndex], out _)) continue;

            var nextTableIndex = FindNextTableIndex(lines, lineIndex + 1);
            var hookBlockEndIndex = nextTableIndex < 0 ? lines.Length : nextTableIndex;
            if (HookBlockContainsCommand(lines, lineIndex + 1, hookBlockEndIndex, IsLidGuardCodexHookCommand)) return true;
        }

        return false;
    }

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

    private static string RemoveManagedBlockMarkerLines(string content)
    {
        var lines = SplitLines(content);
        var remainingLines = new List<string>();
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.Equals(ManagedBlockStartMarker, StringComparison.Ordinal)) continue;
            if (trimmedLine.Equals(ManagedBlockEndMarker, StringComparison.Ordinal)) continue;
            remainingLines.Add(line);
        }

        var updatedContent = JoinLines([.. remainingLines]).TrimEnd();
        return string.IsNullOrWhiteSpace(updatedContent) ? string.Empty : updatedContent + Environment.NewLine;
    }

    private static string UpsertManagedHookCommand(string content, string hookEventName, string hookCommand, string statusMessage)
    {
        var tomlCommandLiteral = ToTomlStringLiteral(hookCommand);
        var replacementLines = CreateHookCommandTableLines(hookEventName, tomlCommandLiteral, statusMessage);
        var lines = SplitLines(content);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!IsHookCommandTableHeader(lines[lineIndex], hookEventName)) continue;

            var nextTableIndex = FindNextTableIndex(lines, lineIndex + 1);
            var hookBlockEndIndex = nextTableIndex < 0 ? lines.Length : nextTableIndex;
            if (!HookBlockContainsCommand(lines, lineIndex + 1, hookBlockEndIndex, IsLidGuardCodexHookCommand)) continue;

            var updatedLines = new List<string>(lines);
            updatedLines.RemoveRange(lineIndex, hookBlockEndIndex - lineIndex);
            updatedLines.InsertRange(lineIndex, replacementLines);
            var updatedContent = JoinLines([.. updatedLines]).TrimEnd();
            return string.IsNullOrWhiteSpace(updatedContent) ? string.Empty : updatedContent + Environment.NewLine;
        }

        var builder = new StringBuilder(content.TrimEnd());
        if (builder.Length > 0) builder.AppendLine();
        if (builder.Length > 0) builder.AppendLine();
        foreach (var line in CreateHookBlockLines(hookEventName, tomlCommandLiteral, statusMessage)) builder.AppendLine(line);
        return builder.ToString();
    }

    private static bool HookBlockContainsCommand(IReadOnlyList<string> lines, int startIndex, int endIndex, Func<string, bool> commandPredicate)
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

    private static bool TryGetManagedHookStatusMessage(string hookEventName, out string statusMessage)
    {
        statusMessage = hookEventName switch
        {
            CodexHookEventNames.UserPromptSubmit => LidGuardText.HookStatusMessageStartingTurnProtection,
            CodexHookEventNames.PermissionRequest => LidGuardText.HookStatusMessageRespondingToClosedLidPermissionRequest,
            CodexHookEventNames.Stop => LidGuardText.HookStatusMessageStoppingSessionProtection,
            CodexHookEventNames.SessionEnd => LidGuardText.HookStatusMessageStoppingSessionProtection,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(statusMessage);
    }

    private static bool UpsertStatusMessageLine(List<string> lines, int startIndex, int endIndex, string statusMessage)
    {
        var statusMessageLine = $"statusMessage = {ToTomlStringLiteral(statusMessage)}";
        for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++)
        {
            if (!TryReadTomlKey(lines[lineIndex], out var key)) continue;
            if (!key.Equals("statusMessage", StringComparison.Ordinal)) continue;

            var updatedLine = GetLineIndentation(lines[lineIndex]) + statusMessageLine;
            if (lines[lineIndex].Equals(updatedLine, StringComparison.Ordinal)) return false;

            lines[lineIndex] = updatedLine;
            return true;
        }

        var commandLineIndex = FindTomlKeyLineIndex(lines, startIndex, endIndex, "command");
        var insertionIndex = commandLineIndex >= 0 ? commandLineIndex + 1 : endIndex;
        var indentation = commandLineIndex >= 0 ? GetLineIndentation(lines[commandLineIndex]) : string.Empty;
        lines.Insert(insertionIndex, indentation + statusMessageLine);
        return true;
    }

    private static int FindTomlKeyLineIndex(IReadOnlyList<string> lines, int startIndex, int endIndex, string key)
    {
        for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++)
        {
            if (TryReadTomlKey(lines[lineIndex], out var candidateKey) && candidateKey.Equals(key, StringComparison.Ordinal)) return lineIndex;
        }

        return -1;
    }

    private static bool TryReadTomlKey(string line, out string key)
    {
        key = string.Empty;
        var trimmedLine = line.Trim();
        var separatorIndex = trimmedLine.IndexOf('=');
        if (separatorIndex < 0) return false;

        key = trimmedLine[..separatorIndex].Trim();
        return !string.IsNullOrWhiteSpace(key);
    }

    private static string GetLineIndentation(string line)
    {
        var characterIndex = 0;
        while (characterIndex < line.Length && char.IsWhiteSpace(line[characterIndex])) characterIndex++;
        return line[..characterIndex];
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

using System.Text;
using System.Text.Json;
using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public static class CodexHookConfigTomlDocument
{
    public const string ManagedBlockStartMarker = "# <LidGuard Codex hook start>";
    public const string ManagedBlockEndMarker = "# <LidGuard Codex hook end>";

    private const string FeaturesSectionHeader = "[features]";
    private const string CodexHooksFeatureKey = "codex_hooks";
    private const string StartStatusMessage = "Starting LidGuard turn protection";
    private const string PermissionRequestStatusMessage = "Responding to permission request";
    private const string StopStatusMessage = "Stopping LidGuard session protection";
    private static readonly string[] s_stopHookEventNames = [CodexHookEventNames.Stop, CodexHookEventNames.SessionEnd];

    public static string CreateManagedHookBlock(string hookCommand)
    {
        var tomlCommandLiteral = ToTomlStringLiteral(hookCommand);
        var builder = new StringBuilder();

        builder.AppendLine(ManagedBlockStartMarker);
        AppendHookBlock(builder, CodexHookEventNames.UserPromptSubmit, tomlCommandLiteral, StartStatusMessage);
        builder.AppendLine();
        AppendHookBlock(builder, CodexHookEventNames.PermissionRequest, tomlCommandLiteral, PermissionRequestStatusMessage);
        foreach (var hookEventName in s_stopHookEventNames)
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
        var hasCurrentManagedBlock = content.Contains(CreateManagedHookBlock(hookCommand), StringComparison.Ordinal);
        var hasUserPromptSubmitHook = ContainsHookBlock(content, CodexHookEventNames.UserPromptSubmit);
        var hasStopHook = ContainsHookBlock(content, CodexHookEventNames.Stop);
        var hasPermissionRequestHook = ContainsHookBlock(content, CodexHookEventNames.PermissionRequest);
        var hasSessionEndHook = ContainsHookBlock(content, CodexHookEventNames.SessionEnd);
        var hasAllStopHooks = hasStopHook && hasSessionEndHook;
        var expectedHookCommandLiteral = ToTomlStringLiteral(hookCommand);
        var hasExpectedHookCommand = content.Contains(hookCommand, StringComparison.Ordinal) || content.Contains(expectedHookCommandLiteral, StringComparison.Ordinal);
        var isInstalled = hasFeatureFlag && hasUserPromptSubmitHook && hasPermissionRequestHook && hasAllStopHooks && hasExpectedHookCommand && (!hasManagedBlock || hasCurrentManagedBlock);
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
}

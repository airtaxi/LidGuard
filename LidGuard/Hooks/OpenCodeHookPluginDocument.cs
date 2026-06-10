using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public static class OpenCodeHookPluginDocument
{
    public const string ManagedBlockStartMarker = "// <LidGuard OpenCode plugin start>";
    public const string ManagedBlockEndMarker = "// <LidGuard OpenCode plugin end>";

    private const string HookCommandPlaceholder = "__LIDGUARD_HOOK_COMMAND_JSON__";
    private const string ManagedPluginVersionMarker = "// LidGuard OpenCode plugin version: 1";
    private const string PluginTemplateResourceName = "LidGuard.Assets.OpenCode.lidguard.js";

    private static readonly JavaScriptEncoder s_jsonEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    public static string CreateManagedPlugin(string hookCommand)
    {
        var template = ReadPluginTemplate();
        return template.Replace(HookCommandPlaceholder, ToJavaScriptStringLiteral(hookCommand), StringComparison.Ordinal);
    }

    public static HookInstallationInspection InspectPlugin(string configurationFilePath, string hookExecutablePath, string hookCommand, string content, bool configurationFileExists)
    {
        var hasManagedBlock = HasManagedPluginBlock(content);
        var hasManagedHookEntries = HasAnyLidGuardOpenCodeHookCommand(content);
        var hasExpectedHookCommand = content.Contains(ToJavaScriptStringLiteral(hookCommand), StringComparison.Ordinal);
        var hasExpectedPluginVersion = content.Contains(ManagedPluginVersionMarker, StringComparison.Ordinal);
        var isInstalled = configurationFileExists && hasManagedBlock && hasManagedHookEntries && hasExpectedHookCommand && hasExpectedPluginVersion;
        var status = isInstalled ? HookInstallationStatus.Installed : hasManagedHookEntries ? HookInstallationStatus.NeedsUpdate : HookInstallationStatus.NotInstalled;
        var message = isInstalled ? "OpenCode hook is installed." : hasManagedHookEntries ? "OpenCode hook is installed but needs update." : "OpenCode hook is not installed.";

        return new HookInstallationInspection
        {
            Provider = AgentProvider.OpenCode,
            Status = status,
            ConfigurationFilePath = configurationFilePath,
            HookExecutablePath = hookExecutablePath,
            HookCommand = hookCommand,
            ConfigurationFileExists = configurationFileExists,
            Checks = new Dictionary<HookInstallationCheck, bool>
            {
                [HookInstallationCheck.ManagedBlock] = hasManagedBlock,
                [HookInstallationCheck.ManagedHookEntries] = hasManagedHookEntries,
                [HookInstallationCheck.ExpectedHookCommand] = hasExpectedHookCommand
            },
            Message = message
        };
    }

    public static string InstallManagedPlugin(string hookCommand) => CreateManagedPlugin(hookCommand);

    public static bool TryRemoveManagedPlugin(string content, out string updatedContent, out bool changed, out string message)
    {
        updatedContent = content;
        changed = false;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(content)) return true;
        if (!HasManagedPluginBlock(content) && HasAnyLidGuardOpenCodeHookCommand(content))
        {
            message = "OpenCode hook is already installed outside the LidGuard managed block.";
            return false;
        }

        if (!HasManagedPluginBlock(content)) return true;

        updatedContent = string.Empty;
        changed = true;
        return true;
    }

    public static bool HasManagedPluginBlock(string content) => !string.IsNullOrWhiteSpace(content) && content.Contains(ManagedBlockStartMarker, StringComparison.Ordinal) && content.Contains(ManagedBlockEndMarker, StringComparison.Ordinal);

    public static bool HasAnyLidGuardOpenCodeHookCommand(string content) => !string.IsNullOrWhiteSpace(content) && content.Contains("opencode-hook", StringComparison.OrdinalIgnoreCase) && content.Contains("LidGuardOpenCodePlugin", StringComparison.Ordinal);

    private static string ToJavaScriptStringLiteral(string value) => $"\"{JsonEncodedText.Encode(value, s_jsonEncoder).ToString()}\"";

    private static string ReadPluginTemplate()
    {
        var assembly = typeof(OpenCodeHookPluginDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(PluginTemplateResourceName) ?? throw new InvalidOperationException($"Missing embedded OpenCode plugin template resource: {PluginTemplateResourceName}");
        using var streamReader = new StreamReader(stream);
        return streamReader.ReadToEnd();
    }
}

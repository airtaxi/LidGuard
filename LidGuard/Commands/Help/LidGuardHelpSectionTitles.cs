using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class LidGuardHelpSectionTitles
{
    internal static string Usage => LocalizationService.GetString("HelpSectionUsage");
    internal static string SessionControl => LocalizationService.GetString("HelpSectionSessionControl");
    internal static string SettingsAndSuspend => LocalizationService.GetString("HelpSectionSettingsAndSuspend");
    internal static string Diagnostics => LocalizationService.GetString("HelpSectionDiagnostics");
    internal static string HookIntegration => LocalizationService.GetString("HelpSectionHookIntegration");
    internal static string McpIntegration => LocalizationService.GetString("HelpSectionMcpIntegration");
    internal static string ManagedAndInternalCommands => LocalizationService.GetString("HelpSectionManagedAndInternalCommands");
    internal static string PathsAndNotes => LocalizationService.GetString("HelpSectionPathsAndNotes");
}

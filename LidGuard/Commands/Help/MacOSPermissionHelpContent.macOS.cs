using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class MacOSPermissionHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return new LidGuardHelpCommandEntry(
            MacOSPermissionCommand.CommandName,
            [],
            LidGuardHelpSectionTitles.Diagnostics,
            LocalizationService.GetString("Help_MacOSPermission_Description"),
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} status",
                    LocalizationService.GetString("Help_MacOSPermission_StatusDescription"),
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} check",
                    LocalizationService.GetString("Help_MacOSPermission_CheckDescription"),
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} install",
                    LocalizationService.GetString("Help_MacOSPermission_InstallDescription"),
                    [],
                    [
                        LocalizationService.GetString("Help_MacOSPermission_InstallNote"),
                        LocalizationService.GetString("Help_MacOSPermission_ManagedRuleNote")
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} remove",
                    LocalizationService.GetString("Help_MacOSPermission_RemoveDescription"),
                    [],
                    [
                        LocalizationService.GetString("Help_MacOSPermission_RemoveNote")
                    ])
            ]);
    }
}

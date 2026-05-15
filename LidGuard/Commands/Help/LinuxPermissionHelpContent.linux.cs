using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class LinuxPermissionHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return new LidGuardHelpCommandEntry(
            LinuxPermissionCommand.CommandName,
            [],
            LidGuardHelpSectionTitles.Diagnostics,
            LocalizationService.GetString("Help_LinuxPermission_Description"),
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} status",
                    LocalizationService.GetString("Help_LinuxPermission_StatusDescription"),
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} check",
                    LocalizationService.GetString("Help_LinuxPermission_CheckDescription"),
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} install",
                    LocalizationService.GetString("Help_LinuxPermission_InstallDescription"),
                    [],
                    [
                        LocalizationService.GetString("Help_LinuxPermission_InstallNote")
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} remove",
                    LocalizationService.GetString("Help_LinuxPermission_RemoveDescription"),
                    [],
                    [
                        LocalizationService.GetString("Help_LinuxPermission_RemoveNote")
                    ])
            ]);
    }
}

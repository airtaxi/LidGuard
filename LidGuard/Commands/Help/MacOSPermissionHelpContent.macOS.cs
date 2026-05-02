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
            "Inspect or manage macOS sudoers permissions for pmset and powermetrics operations.",
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} status",
                    "Print the current macOS permission environment without making changes.",
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} check",
                    "Verify required macOS runtime operations without requesting an actual sleep or hibernate.",
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} install",
                    "Install a LidGuard-managed sudoers rule for the current user.",
                    [],
                    [
                        "This subcommand uses sudo for the one-time administrator write to /private/etc/sudoers.d/lidguard.",
                        "The managed rule permits only LidGuard's pmset disablesleep, pmset hibernatemode, and powermetrics SMC sample commands."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {MacOSPermissionCommand.CommandName} remove",
                    "Remove the LidGuard-managed sudoers rule when that exact managed rule file is present.",
                    [],
                    [
                        "The sudoers file is not removed if it does not contain LidGuard's managed markers."
                    ])
            ]);
    }
}

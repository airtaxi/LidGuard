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
            "Inspect or manage Linux polkit permissions for systemd/logind suspend and inhibitor operations.",
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} status",
                    "Print the current Linux permission environment without making changes.",
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} check",
                    "Verify required Linux runtime operations without requesting an actual suspend or hibernate.",
                    [],
                    []),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} install",
                    "Install a LidGuard-managed polkit rule for the current user.",
                    [],
                    [
                        "This subcommand uses sudo for the one-time administrator write to /etc/polkit-1/rules.d/49-lidguard.rules."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} {LinuxPermissionCommand.CommandName} remove",
                    "Remove the LidGuard-managed polkit rule when that exact managed rule file is present.",
                    [],
                    [
                        "The rule file is not removed if it does not contain LidGuard's managed markers."
                    ])
            ]);
    }
}

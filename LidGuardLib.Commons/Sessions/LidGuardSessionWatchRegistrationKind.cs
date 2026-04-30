namespace LidGuardLib.Commons.Sessions;

public enum LidGuardSessionWatchRegistrationKind
{
    None = 0,
    ExplicitWatchedProcessIdentifier = 1,
    WorkingDirectoryFallback = 2,
    CodexShellHostedWorkingDirectoryFallback = 3
}

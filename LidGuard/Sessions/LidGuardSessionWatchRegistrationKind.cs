namespace LidGuard.Sessions;

public enum LidGuardSessionWatchRegistrationKind
{
    None = 0,
    ExplicitWatchedProcessIdentifier = 1,
    WorkingDirectoryFallback = 2,
    CodexCliWorkingDirectoryFallback = 3
}

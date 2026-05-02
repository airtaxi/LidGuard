namespace LidGuard.Power;

public readonly record struct LidActionBackup(
    Guid PowerSchemeIdentifier,
    bool IncludesAlternatingCurrent,
    LidAction AlternatingCurrentAction,
    bool IncludesDirectCurrent,
    LidAction DirectCurrentAction);

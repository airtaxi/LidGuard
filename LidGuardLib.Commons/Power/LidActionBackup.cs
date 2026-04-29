namespace LidGuardLib.Commons.Power;

public readonly record struct LidActionBackup(
    Guid PowerSchemeIdentifier,
    bool IncludesAlternatingCurrent,
    LidAction AlternatingCurrentAction,
    bool IncludesDirectCurrent,
    LidAction DirectCurrentAction);

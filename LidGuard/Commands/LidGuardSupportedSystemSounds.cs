namespace LidGuard.Commands;

internal static class LidGuardSupportedSystemSounds
{
    private static readonly string[] s_names = ["Asterisk", "Beep", "Exclamation", "Hand", "Question"];

    public static IReadOnlyList<string> Names => s_names;

    public static string Describe() => string.Join(", ", s_names);
}

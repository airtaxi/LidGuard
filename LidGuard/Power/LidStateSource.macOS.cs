using LidGuard.Platform;
using LidGuard.Services;

namespace LidGuard.Power;

internal sealed class LidStateSource : ILidStateSource
{
    private static readonly TimeSpan s_ioregTimeout = TimeSpan.FromSeconds(3);

    public LidSwitchState CurrentState => ReadCurrentState();

    public static LidSwitchState ParseClamshellState(string ioregOutput)
    {
        if (string.IsNullOrWhiteSpace(ioregOutput)) return LidSwitchState.Unknown;

        foreach (var line in ioregOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains("AppleClamshellState", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains("Yes", StringComparison.OrdinalIgnoreCase) || line.Contains("true", StringComparison.OrdinalIgnoreCase)) return LidSwitchState.Closed;
            if (line.Contains("No", StringComparison.OrdinalIgnoreCase) || line.Contains("false", StringComparison.OrdinalIgnoreCase)) return LidSwitchState.Open;
        }

        return LidSwitchState.Unknown;
    }

    private static LidSwitchState ReadCurrentState()
    {
        if (!MacOSCommandPathResolver.TryFindExecutable("ioreg", out var ioregPath)) return LidSwitchState.Unknown;

        var commandResult = MacOSCommandRunner.Run(
            ioregPath,
            ["-r", "-k", "AppleClamshellState", "-d", "1"],
            s_ioregTimeout);
        if (!commandResult.Succeeded) return LidSwitchState.Unknown;

        return ParseClamshellState(commandResult.StandardOutput);
    }
}

using LidGuard.Platform;
using LidGuard.Results;

namespace LidGuard.Power;

internal static class MacOSPowerSettings
{
    public const int HibernateModePlainSleep = 0;
    public const int HibernateModeSafeSleep = 3;
    public const int HibernateModeDiskOnly = 25;
    private static readonly TimeSpan s_pmsetTimeout = TimeSpan.FromSeconds(10);

    public static LidGuardOperationResult<bool> ReadSleepDisabled()
    {
        var readResult = ReadCurrentSetting("SleepDisabled", out var sleepDisabledValue, false);
        if (!readResult.Succeeded) return LidGuardOperationResult<bool>.Failure(readResult.Message, readResult.NativeErrorCode);

        return LidGuardOperationResult<bool>.Success(sleepDisabledValue != 0);
    }

    public static LidGuardOperationResult SetSleepDisabled(bool disabled)
    {
        var commandResult = RunPrivilegedPmset(["-a", "disablesleep", disabled ? "1" : "0"]);
        if (commandResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(
            CreatePrivilegedPmsetFailureMessage(commandResult, "pmset disablesleep"),
            commandResult.ExitCode);
    }

    public static LidGuardOperationResult<int> ReadHibernateMode()
    {
        var readResult = ReadCurrentSetting("hibernatemode", out var hibernateMode, true);
        if (!readResult.Succeeded) return LidGuardOperationResult<int>.Failure(readResult.Message, readResult.NativeErrorCode);

        return LidGuardOperationResult<int>.Success(hibernateMode);
    }

    public static LidGuardOperationResult SetHibernateMode(int hibernateMode)
    {
        if (!IsSupportedHibernateMode(hibernateMode)) return LidGuardOperationResult.Failure($"Unsupported macOS hibernatemode value: {hibernateMode}.");

        var commandResult = RunPrivilegedPmset(["-a", "hibernatemode", hibernateMode.ToString()]);
        if (commandResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(
            CreatePrivilegedPmsetFailureMessage(commandResult, "pmset hibernatemode"),
            commandResult.ExitCode);
    }

    public static LidGuardOperationResult SleepNow()
    {
        if (!MacOSCommandPathResolver.TryFindExecutable("pmset", out var pmsetPath))
            return LidGuardOperationResult.Failure("pmset was not found on PATH. LidGuard macOS suspend support requires /usr/bin/pmset.");

        var commandResult = MacOSCommandRunner.Run(pmsetPath, ["sleepnow"], TimeSpan.FromSeconds(30));
        if (commandResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(commandResult.CreateFailureMessage("pmset sleepnow"), commandResult.ExitCode);
    }

    public static bool IsSupportedHibernateMode(int hibernateMode)
        => hibernateMode is HibernateModePlainSleep or HibernateModeSafeSleep or HibernateModeDiskOnly;

    public static bool TryParseIntegerSetting(string pmsetOutput, string settingName, out int settingValue)
    {
        settingValue = 0;
        if (string.IsNullOrWhiteSpace(pmsetOutput) || string.IsNullOrWhiteSpace(settingName)) return false;

        foreach (var line in pmsetOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2) continue;
            if (!fields[0].Equals(settingName, StringComparison.OrdinalIgnoreCase)) continue;

            return int.TryParse(fields[1], out settingValue);
        }

        return false;
    }

    public static MacOSCommandResult RunPrivilegedCommand(string commandName, IEnumerable<string> arguments, TimeSpan timeout = default)
    {
        if (!MacOSCommandPathResolver.TryFindExecutable(commandName, out var commandPath))
            return MacOSCommandResult.Failure($"{commandName} was not found on PATH.");

        if (IsRootUser()) return MacOSCommandRunner.Run(commandPath, arguments, timeout);

        if (!MacOSCommandPathResolver.TryFindExecutable("sudo", out var sudoPath))
            return MacOSCommandResult.Failure("sudo was not found on PATH. Run macos-permission install or run LidGuard as root for privileged macOS power operations.");

        var sudoArguments = new List<string> { "-n", commandPath };
        sudoArguments.AddRange(arguments ?? Array.Empty<string>());
        return MacOSCommandRunner.Run(sudoPath, sudoArguments, timeout);
    }

    private static MacOSCommandResult RunPrivilegedPmset(IEnumerable<string> arguments)
        => RunPrivilegedCommand("pmset", arguments, s_pmsetTimeout);

    private static LidGuardOperationResult ReadCurrentSetting(string settingName, out int settingValue, bool requirePresence)
    {
        settingValue = 0;
        if (!MacOSCommandPathResolver.TryFindExecutable("pmset", out var pmsetPath))
            return LidGuardOperationResult.Failure("pmset was not found on PATH. LidGuard macOS power support requires /usr/bin/pmset.");

        var commandResult = MacOSCommandRunner.Run(pmsetPath, ["-g"], s_pmsetTimeout);
        if (!commandResult.Succeeded) return LidGuardOperationResult.Failure(commandResult.CreateFailureMessage("pmset -g"), commandResult.ExitCode);
        if (TryParseIntegerSetting(commandResult.StandardOutput, settingName, out settingValue)) return LidGuardOperationResult.Success();
        if (TryParseImplicitSleepDisabledSetting(commandResult.StandardOutput, settingName, out settingValue)) return LidGuardOperationResult.Success();
        if (!requirePresence) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure($"pmset -g did not report {settingName}.");
    }

    private static bool TryParseImplicitSleepDisabledSetting(string pmsetOutput, string settingName, out int settingValue)
    {
        settingValue = 0;
        if (!settingName.Equals("SleepDisabled", StringComparison.OrdinalIgnoreCase)) return false;
        if (!pmsetOutput.Contains("sleep prevented by SleepDisabled", StringComparison.OrdinalIgnoreCase)) return false;

        settingValue = 1;
        return true;
    }

    private static string CreatePrivilegedPmsetFailureMessage(MacOSCommandResult commandResult, string commandDisplayName)
    {
        var failureMessage = commandResult.CreateFailureMessage(commandDisplayName);
        if (failureMessage.Contains("a password is required", StringComparison.OrdinalIgnoreCase)
            || failureMessage.Contains("a terminal is required", StringComparison.OrdinalIgnoreCase)
            || failureMessage.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
            || failureMessage.Contains("sorry", StringComparison.OrdinalIgnoreCase))
        {
            return $"{failureMessage} Run `lidguard macos-permission install` to install LidGuard's managed sudoers rule.";
        }

        return failureMessage;
    }

    private static bool IsRootUser()
    {
        if (!MacOSCommandPathResolver.TryFindExecutable("id", out var userIdentifierCommandPath)) return Environment.UserName.Equals("root", StringComparison.Ordinal);

        var userIdentifierResult = MacOSCommandRunner.Run(userIdentifierCommandPath, ["-u"], TimeSpan.FromSeconds(5));
        return userIdentifierResult.Succeeded && userIdentifierResult.StandardOutput.Trim().Equals("0", StringComparison.Ordinal);
    }
}

using LidGuard.Platform;
using LidGuard.Power;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class MacOSPermissionCommand
{
    public const string CommandName = "macos-permission";
    private const string RuleFilePath = "/private/etc/sudoers.d/lidguard";
    private const string ManagedMarker = "Managed by LidGuard macOS permission command.";
    private const string VersionMarker = "LidGuard macOS sudoers rule v1.";
    private static readonly TimeSpan s_checkCommandTimeout = TimeSpan.FromSeconds(8);
    private static readonly string s_installRuleScript =
        "if [ -e \"$2\" ]; then " +
        "if ! grep -Fq -- \"$3\" \"$2\" || ! grep -Fq -- \"$4\" \"$2\"; then " +
        "echo \"Refusing to overwrite unmanaged sudoers file: $2\" >&2; exit 17; " +
        "fi; fi; mkdir -p \"$(dirname \"$2\")\" && install -m 0440 \"$1\" \"$2\"";
    private static readonly string s_removeRuleScript =
        "if [ ! -e \"$1\" ]; then exit 2; fi; " +
        "if ! grep -Fq -- \"$2\" \"$1\" || ! grep -Fq -- \"$3\" \"$1\"; then " +
        "echo \"Refusing to remove unmanaged sudoers file: $1\" >&2; exit 17; " +
        "fi; rm -f \"$1\"";

    public static int Run(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            Console.Error.WriteLine(Format("PermissionSubcommandRequired", CommandName));
            return 1;
        }

        if (arguments.Length == 1 && IsHelpAlias(arguments[0])) return LidGuardCommandConsole.WriteHelpForCommand(CommandName);

        if (arguments.Length > 1)
        {
            Console.Error.WriteLine(LocalizationService.GetFormattedString("CommandUnexpectedArgument", arguments[1]));
            return 1;
        }

        return arguments[0].Trim().ToLowerInvariant() switch
        {
            "status" => WriteStatus(),
            "check" => RunCheck(),
            "install" => InstallRule(),
            "remove" => RemoveRule(),
            _ => WriteUnknownSubcommand(arguments[0])
        };
    }

    private static int WriteStatus()
    {
        var targetUserName = GetTargetUserName();
        var ruleInspection = InspectRule(targetUserName);

        Console.WriteLine(Get("MacOSPermissionStatusTitle"));
        WriteField("PermissionLabelUser", targetUserName);
        WriteField("MacOSPermissionLabelSudoersPath", RuleFilePath);
        WriteField("MacOSPermissionLabelSudoersRule", DescribeRuleStatus(ruleInspection));
        WriteField("MacOSPermissionLabelPrivilegedPmsetUsability", DescribePrivilegedPmsetUsability());
        WriteField("MacOSPermissionLabelCaffeinate", DescribeExecutableAvailability("caffeinate"));
        WriteField("MacOSPermissionLabelPmset", DescribeExecutableAvailability("pmset"));
        WriteField("MacOSPermissionLabelPowermetrics", DescribeExecutableAvailability("powermetrics"));
        WriteField("MacOSPermissionLabelIoreg", DescribeExecutableAvailability("ioreg"));
        WriteField("MacOSPermissionLabelSystemProfiler", DescribeExecutableAvailability("system_profiler"));
        WriteField("MacOSPermissionLabelSleepDisabled", DescribeSleepDisabled());
        WriteField("MacOSPermissionLabelHibernateMode", DescribeHibernateMode());
        return 0;
    }

    private static int RunCheck()
    {
        var succeeded = true;
        Console.WriteLine(Get("MacOSPermissionCheckTitle"));

        var assertionResult = CaffeinateAssertion.TryAcquire(["-i"]);
        if (assertionResult.Succeeded)
        {
            assertionResult.Value.Dispose();
            WriteCheckLine("MacOSPermissionCheckCaffeinateAcquireRelease", Get("PermissionResultOk"));
        }
        else
        {
            succeeded = false;
            WriteCheckLine("MacOSPermissionCheckCaffeinateAcquireRelease", Format("PermissionResultFailed", assertionResult.Message));
        }

        succeeded &= WritePmsetReadCheck();
        succeeded &= WriteSleepDisabledWriteCheck();
        succeeded &= WriteHibernateModeWriteCheck();
        succeeded &= WritePowermetricsCheck();
        return succeeded ? 0 : 1;
    }

    private static int InstallRule()
    {
        var targetUserName = GetTargetUserName();
        var ruleContent = CreateRuleContent(targetUserName);
        if (!TryValidateRuleContent(ruleContent, out var validationMessage))
        {
            Console.Error.WriteLine(validationMessage);
            return 1;
        }

        if (IsRootUser())
        {
            var ruleInspection = InspectRule(targetUserName);
            if (!ruleInspection.InspectionSucceeded)
            {
                Console.Error.WriteLine(Format("MacOSPermissionInspectSudoersRuleFailed", ruleInspection.Message));
                return 1;
            }

            if (ruleInspection.Exists && !ruleInspection.IsManaged)
            {
                Console.Error.WriteLine(Format("MacOSPermissionRefusingOverwriteUnmanagedSudoersRule", RuleFilePath));
                return 1;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RuleFilePath) ?? "/private/etc/sudoers.d");
                File.WriteAllText(RuleFilePath, ruleContent);
                if (OperatingSystem.IsMacOS()) File.SetUnixFileMode(RuleFilePath, UnixFileMode.UserRead | UnixFileMode.GroupRead);
                Console.WriteLine(Format("MacOSPermissionSudoersRuleInstalled", targetUserName, RuleFilePath));
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(Format("MacOSPermissionInstallSudoersRuleFailed", exception.Message));
                return 1;
            }
        }

        if (!MacOSCommandPathResolver.TryFindExecutable("sudo", out var sudoExecutablePath))
        {
            Console.Error.WriteLine(Get("PermissionSudoNotFound"));
            return 1;
        }

        var temporaryRuleFilePath = Path.Combine(Path.GetTempPath(), $"lidguard-sudoers-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(temporaryRuleFilePath, ruleContent);
            var installResult = MacOSCommandRunner.Run(sudoExecutablePath, ["sh", "-c", s_installRuleScript, "lidguard-rule-install", temporaryRuleFilePath, RuleFilePath, ManagedMarker, VersionMarker], TimeSpan.FromMinutes(2));
            if (!installResult.Succeeded)
            {
                Console.Error.WriteLine(installResult.CreateFailureMessage("sudo install"));
                return 1;
            }

            Console.WriteLine(Format("MacOSPermissionSudoersRuleInstalled", targetUserName, RuleFilePath));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(Format("MacOSPermissionPrepareSudoersRuleFailed", exception.Message));
            return 1;
        }
        finally { TryDeleteTemporaryFile(temporaryRuleFilePath); }
    }

    private static int RemoveRule()
    {
        var targetUserName = GetTargetUserName();
        if (IsRootUser())
        {
            var ruleInspection = InspectRule(targetUserName);
            if (!ruleInspection.InspectionSucceeded)
            {
                Console.Error.WriteLine(Format("MacOSPermissionInspectSudoersRuleFailed", ruleInspection.Message));
                return 1;
            }

            if (!ruleInspection.Exists)
            {
                Console.WriteLine(Format("MacOSPermissionSudoersRuleNotInstalled", RuleFilePath));
                return 0;
            }

            if (!ruleInspection.IsManaged)
            {
                Console.Error.WriteLine(Format("MacOSPermissionRefusingRemoveUnmanagedSudoersRule", RuleFilePath));
                return 1;
            }

            try
            {
                File.Delete(RuleFilePath);
                Console.WriteLine(Format("MacOSPermissionSudoersRuleRemoved", RuleFilePath));
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(Format("MacOSPermissionRemoveSudoersRuleFailed", exception.Message));
                return 1;
            }
        }

        if (!MacOSCommandPathResolver.TryFindExecutable("sudo", out var sudoExecutablePath))
        {
            Console.Error.WriteLine(Get("PermissionSudoNotFound"));
            return 1;
        }

        var removeResult = MacOSCommandRunner.Run(sudoExecutablePath, ["sh", "-c", s_removeRuleScript, "lidguard-rule-remove", RuleFilePath, ManagedMarker, VersionMarker], TimeSpan.FromMinutes(2));
        if (removeResult.Started && removeResult.ExitCode == 2)
        {
            Console.WriteLine(Format("MacOSPermissionSudoersRuleNotInstalled", RuleFilePath));
            return 0;
        }

        if (!removeResult.Succeeded)
        {
            Console.Error.WriteLine(removeResult.CreateFailureMessage("sudo rm"));
            return 1;
        }

        Console.WriteLine(Format("MacOSPermissionSudoersRuleRemoved", RuleFilePath));
        return 0;
    }

    private static bool WritePmsetReadCheck()
    {
        if (!MacOSCommandPathResolver.TryFindExecutable("pmset", out var pmsetPath))
        {
            WriteCheckLine("MacOSPermissionCheckPmsetRead", Format("PermissionResultFailed", Format("PermissionExecutableNotFound", "pmset")));
            return false;
        }

        var commandResult = MacOSCommandRunner.Run(pmsetPath, ["-g"], s_checkCommandTimeout);
        if (commandResult.Succeeded)
        {
            WriteCheckLine("MacOSPermissionCheckPmsetRead", Get("PermissionResultOk"));
            return true;
        }

        WriteCheckLine("MacOSPermissionCheckPmsetRead", Format("PermissionResultFailed", commandResult.CreateFailureMessage("pmset -g")));
        return false;
    }

    private static bool WriteSleepDisabledWriteCheck()
    {
        var readResult = MacOSPowerSettings.ReadSleepDisabled();
        if (!readResult.Succeeded)
        {
            WriteCheckLine("MacOSPermissionCheckPrivilegedPmsetDisableSleep", Format("PermissionResultUnavailable", readResult.Message));
            return false;
        }

        var writeResult = MacOSPowerSettings.SetSleepDisabled(readResult.Value);
        if (writeResult.Succeeded)
        {
            WriteCheckLine("MacOSPermissionCheckPrivilegedPmsetDisableSleep", Get("PermissionResultOk"));
            return true;
        }

        WriteCheckLine("MacOSPermissionCheckPrivilegedPmsetDisableSleep", Format("PermissionResultFailed", writeResult.Message));
        return false;
    }

    private static bool WriteHibernateModeWriteCheck()
    {
        var readResult = MacOSPowerSettings.ReadHibernateMode();
        if (!readResult.Succeeded)
        {
            WriteCheckLine("MacOSPermissionCheckPrivilegedPmsetHibernateMode", Format("PermissionResultUnavailable", readResult.Message));
            return false;
        }

        if (!MacOSPowerSettings.IsSupportedHibernateMode(readResult.Value))
        {
            WriteCheckLine("MacOSPermissionCheckPrivilegedPmsetHibernateMode", Format("MacOSPermissionUnsupportedHibernateModeValue", readResult.Value));
            return false;
        }

        var writeResult = MacOSPowerSettings.SetHibernateMode(readResult.Value);
        if (writeResult.Succeeded)
        {
            WriteCheckLine("MacOSPermissionCheckPrivilegedPmsetHibernateMode", Get("PermissionResultOk"));
            return true;
        }

        WriteCheckLine("MacOSPermissionCheckPrivilegedPmsetHibernateMode", Format("PermissionResultFailed", writeResult.Message));
        return false;
    }

    private static bool WritePowermetricsCheck()
    {
        var commandResult = MacOSPowerSettings.RunPrivilegedCommand("powermetrics", ["--samplers", "smc", "-n", "1", "-i", "1000"], s_checkCommandTimeout);
        if (commandResult.Succeeded)
        {
            WriteCheckLine("MacOSPermissionCheckPrivilegedPowermetricsSmcSample", Get("PermissionResultOk"));
            return true;
        }

        WriteCheckLine("MacOSPermissionCheckPrivilegedPowermetricsSmcSample", Format("PermissionResultUnavailable", commandResult.CreateFailureMessage("powermetrics --samplers smc")));
        return false;
    }

    private static string DescribeExecutableAvailability(string commandName) => MacOSCommandPathResolver.TryFindExecutable(commandName, out var executablePath) ? Format("PermissionExecutableAvailable", executablePath) : Get("PermissionExecutableMissing");

    private static string DescribeSleepDisabled()
    {
        var readResult = MacOSPowerSettings.ReadSleepDisabled();
        return readResult.Succeeded ? (readResult.Value ? "1" : "0") : Format("PermissionResultUnavailableParenthesized", readResult.Message);
    }

    private static string DescribeHibernateMode()
    {
        var readResult = MacOSPowerSettings.ReadHibernateMode();
        return readResult.Succeeded ? readResult.Value.ToString() : Format("PermissionResultUnavailableParenthesized", readResult.Message);
    }

    private static string DescribePrivilegedPmsetUsability()
    {
        var readResult = MacOSPowerSettings.ReadSleepDisabled();
        if (!readResult.Succeeded) return Format("PermissionResultUnavailableParenthesized", readResult.Message);

        var writeResult = MacOSPowerSettings.SetSleepDisabled(readResult.Value);
        return writeResult.Succeeded ? Get("PermissionResultOk") : Format("PermissionResultFailedParenthesized", writeResult.Message);
    }

    private static RuleInspection InspectRule(string targetUserName)
    {
        var readResult = ReadRuleContentDirect();

        if (readResult.NotFound) return RuleInspection.NotInstalled();
        if (!readResult.Succeeded) return RuleInspection.Inconclusive(readResult.Message);

        var isManaged = readResult.Content.Contains(ManagedMarker, StringComparison.Ordinal) && readResult.Content.Contains(VersionMarker, StringComparison.Ordinal);
        var isForCurrentUser = readResult.Content.Contains(CreateUserSpecification(targetUserName), StringComparison.Ordinal);
        return new RuleInspection(true, isManaged, isForCurrentUser, true, string.Empty);
    }

    private static string DescribeRuleStatus(RuleInspection ruleInspection)
    {
        if (!ruleInspection.InspectionSucceeded) return Format("MacOSPermissionRuleContentInspectionUnavailable", ruleInspection.Message);
        if (!ruleInspection.Exists) return Get("PermissionRuleNotInstalled");
        if (!ruleInspection.IsManaged) return Get("PermissionRulePresentUnmanaged");
        return ruleInspection.IsForCurrentUser ? Get("PermissionRuleInstalledForCurrentUser") : Get("PermissionRuleInstalledForAnotherUser");
    }

    private static RuleContentReadResult ReadRuleContentDirect()
    {
        try { return RuleContentReadResult.Success(File.ReadAllText(RuleFilePath)); }
        catch (FileNotFoundException) { return RuleContentReadResult.NotFoundResult(); }
        catch (DirectoryNotFoundException) { return RuleContentReadResult.NotFoundResult(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return RuleContentReadResult.Inconclusive(exception.Message); }
    }

    private static string CreateRuleContent(string targetUserName)
    {
        var userSpecification = CreateUserSpecification(targetUserName);
        return $"""
# {ManagedMarker}
# {VersionMarker}
{userSpecification} ALL=(root) NOPASSWD: {string.Join(", ", CreateAllowedCommandLines())}
""";
    }

    private static IEnumerable<string> CreateAllowedCommandLines()
    {
        yield return "/usr/bin/pmset -a disablesleep 0";
        yield return "/usr/bin/pmset -a disablesleep 1";
        yield return "/usr/bin/pmset -a hibernatemode 0";
        yield return "/usr/bin/pmset -a hibernatemode 3";
        yield return "/usr/bin/pmset -a hibernatemode 25";
        yield return "/usr/bin/powermetrics --samplers smc -n 1 -i 1000";
    }

    private static string CreateUserSpecification(string targetUserName)
    {
        if (!string.IsNullOrWhiteSpace(targetUserName) && targetUserName.All(static character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.')) return targetUserName;

        var escapedUserName = (targetUserName ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escapedUserName}\"";
    }

    private static bool TryValidateRuleContent(string ruleContent, out string message)
    {
        message = string.Empty;
        if (!MacOSCommandPathResolver.TryFindExecutable("visudo", out var visudoPath))
        {
            message = Get("MacOSPermissionVisudoNotFound");
            return false;
        }

        var temporaryRuleFilePath = Path.Combine(Path.GetTempPath(), $"lidguard-sudoers-validate-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(temporaryRuleFilePath, ruleContent);
            var validationResult = MacOSCommandRunner.Run(visudoPath, ["-cf", temporaryRuleFilePath], s_checkCommandTimeout);
            if (validationResult.Succeeded) return true;

            message = validationResult.CreateFailureMessage("visudo -cf");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = Format("MacOSPermissionPrepareSudoersValidationFileFailed", exception.Message);
            return false;
        }
        finally { TryDeleteTemporaryFile(temporaryRuleFilePath); }
    }

    private static string GetTargetUserName()
    {
        var currentUserName = Environment.UserName;
        var sudoUserName = Environment.GetEnvironmentVariable("SUDO_USER");
        if (currentUserName.Equals("root", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(sudoUserName)) return sudoUserName.Trim();
        if (!string.IsNullOrWhiteSpace(currentUserName)) return currentUserName;

        var userResult = MacOSCommandPathResolver.TryFindExecutable("whoami", out var whoamiPath) ? MacOSCommandRunner.Run(whoamiPath, [], s_checkCommandTimeout) : MacOSCommandResult.Failure("whoami was not found.");
        return userResult.Succeeded && !string.IsNullOrWhiteSpace(userResult.StandardOutput) ? userResult.StandardOutput.Trim() : Get("PermissionUnknownUser");
    }

    private static bool IsRootUser()
    {
        if (!MacOSCommandPathResolver.TryFindExecutable("id", out var userIdentifierCommandPath)) return Environment.UserName.Equals("root", StringComparison.Ordinal);

        var userIdentifierResult = MacOSCommandRunner.Run(userIdentifierCommandPath, ["-u"], s_checkCommandTimeout);
        return userIdentifierResult.Succeeded && userIdentifierResult.StandardOutput.Trim().Equals("0", StringComparison.Ordinal);
    }

    private static int WriteUnknownSubcommand(string subcommand)
    {
        Console.Error.WriteLine(Format("PermissionUnknownSubcommand", CommandName, subcommand));
        Console.Error.WriteLine(Format("PermissionSubcommandUsage", CommandName));
        return 1;
    }

    private static bool IsHelpAlias(string argument) => argument.Equals("--help", StringComparison.OrdinalIgnoreCase) || argument.Equals("-h", StringComparison.OrdinalIgnoreCase) || argument.Equals("/?", StringComparison.OrdinalIgnoreCase);

    private static void WriteField(string labelResourceName, string value) => Console.WriteLine(Format("ManagementField", Get(labelResourceName), value));

    private static void WriteCheckLine(string labelResourceName, string value) => Console.WriteLine(Format("ManagementField", Get(labelResourceName), value));

    private static string Get(string resourceName) => LocalizationService.GetString(resourceName);

    private static string Format(string resourceName, params object[] arguments) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(resourceName), arguments);

    private static void TryDeleteTemporaryFile(string temporaryRuleFilePath)
    {
        try
        {
            if (File.Exists(temporaryRuleFilePath)) File.Delete(temporaryRuleFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private readonly record struct RuleInspection(bool Exists, bool IsManaged, bool IsForCurrentUser, bool InspectionSucceeded, string Message)
    {
        public static RuleInspection NotInstalled() => new(false, false, false, true, string.Empty);

        public static RuleInspection Inconclusive(string message) => new(false, false, false, false, message);
    }

    private readonly record struct RuleContentReadResult(bool Succeeded, bool NotFound, string Content, string Message)
    {
        public static RuleContentReadResult Success(string content) => new(true, false, content ?? string.Empty, string.Empty);

        public static RuleContentReadResult NotFoundResult() => new(false, true, string.Empty, string.Empty);

        public static RuleContentReadResult Inconclusive(string message) => new(false, false, string.Empty, message ?? string.Empty);
    }
}

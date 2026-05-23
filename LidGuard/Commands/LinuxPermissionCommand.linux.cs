using LidGuard.Platform;
using LidGuard.Power;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class LinuxPermissionCommand
{
    public const string CommandName = "linux-permission";
    private const string RuleFilePath = "/etc/polkit-1/rules.d/49-lidguard.rules";
    private const string ManagedMarker = "Managed by LidGuard Linux permission command.";
    private const string VersionMarker = "LidGuard Linux polkit rule v1.";
    private static readonly TimeSpan s_checkCommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly string s_installRuleScript =
        "if [ -e \"$2\" ]; then " +
        "if ! grep -Fq -- \"$3\" \"$2\" || ! grep -Fq -- \"$4\" \"$2\"; then " +
        "echo \"Refusing to overwrite unmanaged polkit rule file: $2\" >&2; exit 17; " +
        "fi; fi; install -m 0644 \"$1\" \"$2\"";
    private static readonly string s_removeRuleScript =
        "if [ ! -e \"$1\" ]; then exit 2; fi; " +
        "if ! grep -Fq -- \"$2\" \"$1\" || ! grep -Fq -- \"$3\" \"$1\"; then " +
        "echo \"Refusing to remove unmanaged polkit rule file: $1\" >&2; exit 17; " +
        "fi; rm -f \"$1\"";

    private static readonly string[] s_allowedActionIdentifiers =
    [
        "org.freedesktop.login1.suspend",
        "org.freedesktop.login1.suspend-multiple-sessions",
        "org.freedesktop.login1.hibernate",
        "org.freedesktop.login1.hibernate-multiple-sessions",
        "org.freedesktop.login1.inhibit-block-sleep",
        "org.freedesktop.login1.inhibit-block-idle",
        "org.freedesktop.login1.inhibit-handle-lid-switch"
    ];

    public static int Run(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            Console.Error.WriteLine(Format("PermissionSubcommandRequired", CommandName));
            return 1;
        }

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

        Console.WriteLine(Get("LinuxPermissionStatusTitle"));
        WriteField("PermissionLabelUser", targetUserName);
        WriteField("LinuxPermissionLabelPolkitRulePath", RuleFilePath);
        WriteField("LinuxPermissionLabelPolkitRule", DescribeRuleStatus(ruleInspection));
        WriteField("LinuxPermissionLabelSystemdInhibit", DescribeExecutableAvailability("systemd-inhibit"));
        WriteField("LinuxPermissionLabelSystemctl", DescribeExecutableAvailability("systemctl"));
        WriteField("LinuxPermissionLabelLogindCanSuspend", DescribeCapability("CanSuspend"));
        WriteField("LinuxPermissionLabelLogindCanHibernate", DescribeCapability("CanHibernate"));
        return 0;
    }

    private static int RunCheck()
    {
        var succeeded = true;
        Console.WriteLine(Get("LinuxPermissionCheckTitle"));

        var inhibitorResult = SystemdInhibitor.TryAcquire("sleep:idle:handle-lid-switch", "LidGuard Linux permission check is verifying inhibitor access.");
        if (inhibitorResult.Succeeded)
        {
            inhibitorResult.Value.Dispose();
            WriteCheckLine("LinuxPermissionCheckInhibitorAcquireRelease", Get("PermissionResultOk"));
        }
        else
        {
            succeeded = false;
            WriteCheckLine("LinuxPermissionCheckInhibitorAcquireRelease", Format("PermissionResultFailed", inhibitorResult.Message));
        }

        if (LinuxCommandPathResolver.TryFindExecutable("systemctl", out var systemctlPath))
        {
            var systemctlResult = LinuxCommandRunner.Run(systemctlPath, ["--version"], s_checkCommandTimeout);
            if (systemctlResult.Succeeded) WriteCheckLine("LinuxPermissionCheckSystemctlVersion", Get("PermissionResultOk"));
            else
            {
                succeeded = false;
                WriteCheckLine("LinuxPermissionCheckSystemctlVersion", Format("PermissionResultFailed", systemctlResult.CreateFailureMessage("systemctl --version")));
            }
        }
        else
        {
            succeeded = false;
            WriteCheckLine("LinuxPermissionCheckSystemctlVersion", Format("PermissionResultFailed", Format("PermissionExecutableNotFound", "systemctl")));
        }

        succeeded &= WriteCapabilityCheck("CanSuspend");
        succeeded &= WriteCapabilityCheck("CanHibernate");
        return succeeded ? 0 : 1;
    }

    private static int InstallRule()
    {
        var targetUserName = GetTargetUserName();
        var ruleContent = CreateRuleContent(targetUserName);
        if (IsRootUser())
        {
            var ruleInspection = InspectRule(targetUserName);
            if (!ruleInspection.InspectionSucceeded)
            {
                Console.Error.WriteLine(Format("LinuxPermissionInspectPolkitRuleFailed", ruleInspection.Message));
                return 1;
            }

            if (ruleInspection.Exists && !ruleInspection.IsManaged)
            {
                Console.Error.WriteLine(Format("LinuxPermissionRefusingOverwriteUnmanagedPolkitRule", RuleFilePath));
                return 1;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RuleFilePath) ?? "/etc/polkit-1/rules.d");
                File.WriteAllText(RuleFilePath, ruleContent);
                Console.WriteLine(Format("LinuxPermissionPolkitRuleInstalled", targetUserName, RuleFilePath));
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(Format("LinuxPermissionInstallPolkitRuleFailed", exception.Message));
                return 1;
            }
        }

        if (!LinuxCommandPathResolver.TryFindExecutable("sudo", out var sudoExecutablePath))
        {
            Console.Error.WriteLine(Get("PermissionSudoNotFound"));
            return 1;
        }

        var temporaryRuleFilePath = Path.Combine(Path.GetTempPath(), $"lidguard-polkit-{Guid.NewGuid():N}.rules");
        try
        {
            File.WriteAllText(temporaryRuleFilePath, ruleContent);
            var installResult = LinuxCommandRunner.Run(sudoExecutablePath, ["sh", "-c", s_installRuleScript, "lidguard-rule-install", temporaryRuleFilePath, RuleFilePath, ManagedMarker, VersionMarker], TimeSpan.FromMinutes(2));
            if (!installResult.Succeeded)
            {
                Console.Error.WriteLine(installResult.CreateFailureMessage("sudo install"));
                return 1;
            }

            Console.WriteLine(Format("LinuxPermissionPolkitRuleInstalled", targetUserName, RuleFilePath));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(Format("LinuxPermissionPreparePolkitRuleFailed", exception.Message));
            return 1;
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryRuleFilePath);
        }
    }

    private static int RemoveRule()
    {
        var targetUserName = GetTargetUserName();
        if (IsRootUser())
        {
            var ruleInspection = InspectRule(targetUserName);
            if (!ruleInspection.InspectionSucceeded)
            {
                Console.Error.WriteLine(Format("LinuxPermissionInspectPolkitRuleFailed", ruleInspection.Message));
                return 1;
            }

            if (!ruleInspection.Exists)
            {
                Console.WriteLine(Format("LinuxPermissionPolkitRuleNotInstalled", RuleFilePath));
                return 0;
            }

            if (!ruleInspection.IsManaged)
            {
                Console.Error.WriteLine(Format("LinuxPermissionRefusingRemoveUnmanagedPolkitRule", RuleFilePath));
                return 1;
            }

            try
            {
                File.Delete(RuleFilePath);
                Console.WriteLine(Format("LinuxPermissionPolkitRuleRemoved", RuleFilePath));
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(Format("LinuxPermissionRemovePolkitRuleFailed", exception.Message));
                return 1;
            }
        }

        if (!LinuxCommandPathResolver.TryFindExecutable("sudo", out var sudoExecutablePath))
        {
            Console.Error.WriteLine(Get("PermissionSudoNotFound"));
            return 1;
        }

        var removeResult = LinuxCommandRunner.Run(sudoExecutablePath, ["sh", "-c", s_removeRuleScript, "lidguard-rule-remove", RuleFilePath, ManagedMarker, VersionMarker], TimeSpan.FromMinutes(2));
        if (removeResult.Started && removeResult.ExitCode == 2)
        {
            Console.WriteLine(Format("LinuxPermissionPolkitRuleNotInstalled", RuleFilePath));
            return 0;
        }

        if (!removeResult.Succeeded)
        {
            Console.Error.WriteLine(removeResult.CreateFailureMessage("sudo rm"));
            return 1;
        }

        Console.WriteLine(Format("LinuxPermissionPolkitRuleRemoved", RuleFilePath));
        return 0;
    }

    private static bool WriteCapabilityCheck(string capabilityName)
    {
        if (TryQueryLogindCapability(capabilityName, out var capabilityValue, out var message))
        {
            Console.WriteLine(Format("ManagementField", $"logind {capabilityName}", capabilityValue));
            return true;
        }

        Console.WriteLine(Format("ManagementField", $"logind {capabilityName}", Format("PermissionResultUnavailable", message)));
        return false;
    }

    private static string DescribeExecutableAvailability(string commandName)
        => LinuxCommandPathResolver.TryFindExecutable(commandName, out var executablePath) ? Format("PermissionExecutableAvailable", executablePath) : Get("PermissionExecutableMissing");

    private static string DescribeCapability(string capabilityName)
        => TryQueryLogindCapability(capabilityName, out var capabilityValue, out var message) ? capabilityValue : Format("PermissionResultUnavailableParenthesized", message);

    private static bool TryQueryLogindCapability(string capabilityName, out string capabilityValue, out string message)
    {
        capabilityValue = string.Empty;
        message = string.Empty;
        if (!LinuxCommandPathResolver.TryFindExecutable("busctl", out var busctlPath))
        {
            message = Format("PermissionExecutableNotFound", "busctl");
            return false;
        }

        var commandResult = LinuxCommandRunner.Run(busctlPath, ["call", "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", capabilityName], s_checkCommandTimeout);
        if (!commandResult.Succeeded)
        {
            message = commandResult.CreateFailureMessage($"busctl call {capabilityName}");
            return false;
        }

        capabilityValue = ParseBusctlStringValue(commandResult.StandardOutput);
        if (!string.IsNullOrWhiteSpace(capabilityValue)) return true;

        message = Get("LinuxPermissionBusctlUnrecognizedValue");
        return false;
    }

    private static string ParseBusctlStringValue(string output)
    {
        var trimmedOutput = output.Trim();
        var firstQuoteIndex = trimmedOutput.IndexOf('"');
        var lastQuoteIndex = trimmedOutput.LastIndexOf('"');
        if (firstQuoteIndex >= 0 && lastQuoteIndex > firstQuoteIndex) return trimmedOutput[(firstQuoteIndex + 1)..lastQuoteIndex];

        var fields = trimmedOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fields.Length == 0 ? string.Empty : fields[^1].Trim('"');
    }

    private static RuleInspection InspectRule(string targetUserName)
    {
        var readResult = ReadRuleContentDirect();
        if (!readResult.Succeeded && readResult.IsInconclusive)
        {
            var sudoReadResult = ReadRuleContentWithNonInteractiveSudo();
            readResult = sudoReadResult.Succeeded || sudoReadResult.NotFound ? sudoReadResult : RuleContentReadResult.Inconclusive($"{readResult.Message} {sudoReadResult.Message}".Trim());
        }

        if (readResult.NotFound) return RuleInspection.NotInstalled();
        if (!readResult.Succeeded) return RuleInspection.Inconclusive(readResult.Message);

        var isManaged = readResult.Content.Contains(ManagedMarker, StringComparison.Ordinal) && readResult.Content.Contains(VersionMarker, StringComparison.Ordinal);
        var isForCurrentUser = readResult.Content.Contains($"subject.user == \"{EscapeJavaScriptString(targetUserName)}\"", StringComparison.Ordinal);
        return new RuleInspection(true, isManaged, isForCurrentUser, true, string.Empty);
    }

    private static string DescribeRuleStatus(RuleInspection ruleInspection)
    {
        if (!ruleInspection.InspectionSucceeded) return Format("LinuxPermissionRuleUnableToInspect", ruleInspection.Message);
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

    private static RuleContentReadResult ReadRuleContentWithNonInteractiveSudo()
    {
        if (!LinuxCommandPathResolver.TryFindExecutable("sudo", out var sudoExecutablePath)) return RuleContentReadResult.Inconclusive(Get("PermissionSudoNotFoundShort"));

        var commandResult = LinuxCommandRunner.Run(sudoExecutablePath, ["-n", "cat", RuleFilePath], s_checkCommandTimeout);
        if (commandResult.Succeeded) return RuleContentReadResult.Success(commandResult.StandardOutput);

        var failureMessage = commandResult.CreateFailureMessage("sudo -n cat");
        if (failureMessage.Contains("No such file", StringComparison.OrdinalIgnoreCase)) return RuleContentReadResult.NotFoundResult();
        return RuleContentReadResult.Inconclusive(failureMessage);
    }

    private static string CreateRuleContent(string targetUserName)
    {
        static string CreateActionLine(string actionIdentifier, int actionIndex)
        {
            var separator = actionIndex + 1 == s_allowedActionIdentifiers.Length ? string.Empty : ",";
            return $"        \"{actionIdentifier}\"{separator}";
        }

        var escapedUserName = EscapeJavaScriptString(targetUserName);
        var actionLines = string.Join(Environment.NewLine, s_allowedActionIdentifiers.Select(CreateActionLine));
        return $$"""
// {{ManagedMarker}}
// {{VersionMarker}}
polkit.addRule(function(action, subject) {
    var lidGuardActions = [
{{actionLines}}
    ];

    if (subject.user == "{{escapedUserName}}" && lidGuardActions.indexOf(action.id) >= 0) {
        return polkit.Result.YES;
    }
});
""";
    }

    private static string EscapeJavaScriptString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string GetTargetUserName()
    {
        var currentUserName = Environment.UserName;
        var sudoUserName = Environment.GetEnvironmentVariable("SUDO_USER");
        if (currentUserName.Equals("root", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(sudoUserName)) return sudoUserName.Trim();
        if (!string.IsNullOrWhiteSpace(currentUserName)) return currentUserName;

        var userResult = LinuxCommandPathResolver.TryFindExecutable("whoami", out var whoamiPath) ? LinuxCommandRunner.Run(whoamiPath, [], s_checkCommandTimeout) : LinuxCommandResult.Failure("whoami was not found.");
        return userResult.Succeeded && !string.IsNullOrWhiteSpace(userResult.StandardOutput) ? userResult.StandardOutput.Trim() : Get("PermissionUnknownUser");
    }

    private static bool IsRootUser()
    {
        if (!LinuxCommandPathResolver.TryFindExecutable("id", out var userIdentifierCommandPath)) return Environment.UserName.Equals("root", StringComparison.Ordinal);

        var userIdentifierResult = LinuxCommandRunner.Run(userIdentifierCommandPath, ["-u"], s_checkCommandTimeout);
        return userIdentifierResult.Succeeded && userIdentifierResult.StandardOutput.Trim().Equals("0", StringComparison.Ordinal);
    }

    private static int WriteUnknownSubcommand(string subcommand)
    {
        Console.Error.WriteLine(Format("PermissionUnknownSubcommand", CommandName, subcommand));
        Console.Error.WriteLine(Format("PermissionSubcommandUsage", CommandName));
        return 1;
    }

    private static void WriteField(string labelResourceName, string value)
        => Console.WriteLine(Format("ManagementField", Get(labelResourceName), value));

    private static void WriteCheckLine(string labelResourceName, string value)
        => Console.WriteLine(Format("ManagementField", Get(labelResourceName), value));

    private static string Get(string resourceName)
        => LocalizationService.GetString(resourceName);

    private static string Format(string resourceName, params object[] arguments)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(resourceName), arguments);

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

    private readonly record struct RuleContentReadResult(bool Succeeded, bool NotFound, bool IsInconclusive, string Content, string Message)
    {
        public static RuleContentReadResult Success(string content) => new(true, false, false, content ?? string.Empty, string.Empty);

        public static RuleContentReadResult NotFoundResult() => new(false, true, false, string.Empty, string.Empty);

        public static RuleContentReadResult Inconclusive(string message) => new(false, false, true, string.Empty, message ?? string.Empty);
    }
}

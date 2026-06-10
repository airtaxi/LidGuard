using System.ComponentModel;
using System.Diagnostics;
using LidGuard.Hooks;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class WslCommandUtilities
{
    public const string DistroOptionName = "distro";
    public const string WslExecutableName = "wsl.exe";

    public readonly record struct WslContext(string DistroName, string WslExecutablePath);

    public static string GetDistroName(IReadOnlyDictionary<string, string> options) => NormalizeDistroName(CommandOptionReader.GetOption(options, DistroOptionName));

    public static string GetDistroDisplayName(string distroName)
    {
        var normalizedDistroName = NormalizeDistroName(distroName);
        return string.IsNullOrWhiteSpace(normalizedDistroName) ? "default" : normalizedDistroName;
    }

    public static bool TryGetDistroName(IReadOnlyDictionary<string, string> options, out string distroName, out string message)
    {
        distroName = GetDistroName(options);
        if (!distroName.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            message = string.Empty;
            return true;
        }

        message = LocalizationService.GetString("CommandRequiredOption")
            .Replace("{0}", DistroOptionName, StringComparison.Ordinal);
        return false;
    }

    public static bool TryCreateContext(IReadOnlyDictionary<string, string> options, out WslContext context, out string message)
    {
        context = default;
        if (!TryGetDistroName(options, out var distroName, out message)) return false;
        if (!TryValidateWsl(distroName, out message)) return false;
        if (!TryGetWslLidGuardExecutablePath(distroName, out var wslExecutablePath, out message)) return false;

        context = new WslContext(distroName, wslExecutablePath);
        return true;
    }

    public static bool TryValidateWsl(string distroName, out string message)
    {
        distroName = NormalizeDistroName(distroName);
        message = string.Empty;

        var statusResult = RunWslProcess(["--status"]);
        if (statusResult.StartFailed)
        {
            message = LocalizationService.GetString("WslExecutableNotFound");
            return false;
        }

        if (statusResult.ExitCode != 0)
        {
            message = LocalizationService.GetString("WslStatusFailed")
                .Replace("{0}", statusResult.GetDisplayError(), StringComparison.Ordinal);
            return false;
        }

        var validationArguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(distroName))
        {
            validationArguments.Add("--distribution");
            validationArguments.Add(distroName);
        }

        validationArguments.Add("--exec");
        validationArguments.Add("true");

        var distroResult = RunWslProcess(validationArguments);
        if (distroResult.ExitCode == 0) return true;

        message = string.IsNullOrWhiteSpace(distroName) ? LocalizationService.GetString("WslDefaultDistroUnavailable").Replace("{0}", distroResult.GetDisplayError(), StringComparison.Ordinal) : LocalizationService.GetString("WslNamedDistroUnavailable").Replace("{0}", distroName, StringComparison.Ordinal).Replace("{1}", distroResult.GetDisplayError(), StringComparison.Ordinal);
        return false;
    }

    public static bool TryGetWslLidGuardExecutablePath(string distroName, out string wslExecutablePath, out string message)
    {
        wslExecutablePath = string.Empty;
        if (!TryResolveWindowsLidGuardExecutablePath(out var windowsExecutablePath, out message)) return false;

        var result = RunShell(distroName, "wslpath -a \"$1\"", [windowsExecutablePath]);
        if (result.ExitCode != 0)
        {
            message = LocalizationService.GetString("WslPathConversionFailed")
                .Replace("{0}", result.GetDisplayError(), StringComparison.Ordinal);
            return false;
        }

        wslExecutablePath = result.StandardOutput.Trim();
        if (!string.IsNullOrWhiteSpace(wslExecutablePath)) return true;

        message = LocalizationService.GetString("WslPathConversionEmpty");
        return false;
    }

    public static string CreateWslLidGuardCommand(string wslExecutablePath, string commandName) => $"{QuoteBashWord(wslExecutablePath)} {commandName}";

    public static string GetHookCommandName(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.Codex => LidGuardPipeCommands.CodexHook,
            AgentProvider.Claude => LidGuardPipeCommands.ClaudeHook,
            AgentProvider.GitHubCopilot => LidGuardPipeCommands.CopilotHook,
            AgentProvider.OpenCode => LidGuardPipeCommands.OpenCodeHook,
            _ => string.Empty
        };
    }

    public static bool ExecutableReferencesMatch(string executableReference, string expectedExecutableReference) => executableReference.Trim().Equals(expectedExecutableReference.Trim(), StringComparison.Ordinal);

    public static bool FileExists(string distroName, string filePath) => RunShell(distroName, "test -f \"$1\"", [filePath]).ExitCode == 0;

    public static bool PathExists(string distroName, string path) => RunShell(distroName, "test -e \"$1\"", [path]).ExitCode == 0;

    public static WslCommandResult RunCommand(string distroName, string executableName, IReadOnlyList<string> arguments)
    {
        var shellArguments = new List<string> { executableName };
        shellArguments.AddRange(arguments);
        return RunShell(distroName, "exec \"$@\"", shellArguments);
    }

    public static WslCommandResult RunShell(string distroName, string script, IReadOnlyList<string> arguments, string standardInput = "")
    {
        distroName = NormalizeDistroName(distroName);
        var processArguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(distroName))
        {
            processArguments.Add("--distribution");
            processArguments.Add(distroName);
        }

        processArguments.Add("--exec");
        processArguments.Add("sh");
        processArguments.Add("-lc");
        processArguments.Add(script);
        processArguments.Add("lidguard-wsl");
        processArguments.AddRange(arguments);
        return RunWslProcess(processArguments, standardInput);
    }

    public static bool TryCopyFile(string distroName, string sourceFilePath, string destinationFilePath, out string message)
    {
        var result = RunShell(distroName, "cp \"$1\" \"$2\"", [sourceFilePath, destinationFilePath]);
        message = result.GetDisplayError();
        return result.ExitCode == 0;
    }

    public static bool TryNormalizeWslPath(string distroName, string path, out string normalizedPath, out string message)
    {
        normalizedPath = string.Empty;
        message = string.Empty;

        var result = RunShell(distroName, "case \"$1\" in \"~\") printf '%s' \"$HOME\" ;; \"~/\"*) printf '%s/%s' \"$HOME\" \"${1#~/}\" ;; /*) printf '%s' \"$1\" ;; *) printf '%s/%s' \"$PWD\" \"$1\" ;; esac", [path]);
        if (result.ExitCode != 0)
        {
            message = result.GetDisplayError();
            return false;
        }

        normalizedPath = result.StandardOutput.Trim();
        return !string.IsNullOrWhiteSpace(normalizedPath);
    }

    public static bool TryReadTextFile(string distroName, string filePath, out string content, out string message)
    {
        content = string.Empty;
        var result = RunShell(distroName, "cat \"$1\"", [filePath]);
        if (result.ExitCode == 0)
        {
            content = result.StandardOutput;
            message = string.Empty;
            return true;
        }

        message = result.GetDisplayError();
        return false;
    }

    public static bool TryRemoveFile(string distroName, string filePath, out string message)
    {
        var result = RunShell(distroName, "rm -f \"$1\"", [filePath]);
        message = result.GetDisplayError();
        return result.ExitCode == 0;
    }

    public static bool TryResolveDefaultPath(string distroName, string script, out string path, out string message)
    {
        path = string.Empty;
        var result = RunShell(distroName, script, []);
        if (result.ExitCode != 0)
        {
            message = result.GetDisplayError();
            return false;
        }

        path = result.StandardOutput.Trim();
        message = string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    public static bool TryWriteTextFile(string distroName, string filePath, string content, out string message)
    {
        var result = RunShell(distroName, "mkdir -p \"$(dirname \"$1\")\" && cat > \"$1\"", [filePath], content);
        message = result.GetDisplayError();
        return result.ExitCode == 0;
    }

    public static string CreateBackupFilePath(string configurationFilePath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return $"{configurationFilePath}.{timestamp}.bak";
    }

    public static string GetProviderCliExecutableName(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.Codex => "codex",
            AgentProvider.Claude => "claude",
            AgentProvider.GitHubCopilot => "copilot",
            AgentProvider.OpenCode => "opencode",
            _ => string.Empty
        };
    }

    public static bool TryResolveProviderCliDisplayText(string distroName, AgentProvider provider, out bool hasProviderCli, out string providerCliDisplayText)
    {
        var executableName = GetProviderCliExecutableName(provider);
        hasProviderCli = false;
        providerCliDisplayText = executableName;
        if (string.IsNullOrWhiteSpace(executableName)) return false;

        var result = RunShell(distroName, "command -v \"$1\"", [executableName]);
        hasProviderCli = result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);
        if (hasProviderCli) providerCliDisplayText = result.StandardOutput.Trim();
        return hasProviderCli;
    }

    public static bool TryListDistros(out IReadOnlyList<string> distroNames, out string message)
    {
        distroNames = [];
        message = string.Empty;
        var result = RunWslProcess(["--list", "--quiet"]);
        if (result.StartFailed)
        {
            message = result.GetDisplayError();
            return false;
        }

        if (result.ExitCode != 0)
        {
            message = result.GetDisplayError();
            return false;
        }

        distroNames = NormalizeWslConsoleText(result.StandardOutput)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDistroName)
            .Where(distroName => !string.IsNullOrWhiteSpace(distroName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private static string NormalizeDistroName(string distroName) => NormalizeWslConsoleText(distroName).Trim();

    private static string NormalizeWslConsoleText(string value) => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\0", string.Empty, StringComparison.Ordinal);

    private static WslCommandResult RunWslProcess(IReadOnlyList<string> arguments, string standardInput = "")
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = WslExecutableName,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start()) return WslCommandResult.FailedToStart(LocalizationService.GetFormattedString("ManagementFailedToStartProcess", WslExecutableName));

            if (!string.IsNullOrEmpty(standardInput)) process.StandardInput.Write(standardInput);

            process.StandardInput.Close();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return new WslCommandResult(process.ExitCode, standardOutputTask.GetAwaiter().GetResult(), standardErrorTask.GetAwaiter().GetResult(), false);
        }
        catch (Win32Exception exception) { return WslCommandResult.FailedToStart(exception.Message); }
        catch (InvalidOperationException exception) { return WslCommandResult.FailedToStart(exception.Message); }
    }

    private static string QuoteBashWord(string value) => $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    private static bool TryResolveWindowsLidGuardExecutablePath(out string executablePath, out string message)
    {
        executablePath = string.Empty;
        message = string.Empty;

        var executableReference = HookCommandUtilities.GetDefaultMcpExecutableReference();
        if (HookCommandUtilities.TryResolveExecutableReferencePath(executableReference, out var resolvedExecutablePath) && resolvedExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(resolvedExecutablePath))
        {
            executablePath = Path.GetFullPath(resolvedExecutablePath);
            return true;
        }

        message = LocalizationService.GetString("WslLidGuardExecutableNotResolved");
        return false;
    }
}

internal readonly record struct WslCommandResult(int ExitCode, string StandardOutput, string StandardError, bool StartFailed)
{
    public static WslCommandResult FailedToStart(string message) => new(1, string.Empty, message, true);

    public string GetDisplayError()
    {
        var standardError = NormalizeDisplayText(StandardError);
        if (!string.IsNullOrWhiteSpace(standardError)) return standardError;

        var standardOutput = NormalizeDisplayText(StandardOutput);
        if (!string.IsNullOrWhiteSpace(standardOutput)) return standardOutput;

        return LocalizationService.GetString("TextDisplayNone");
    }

    private static string NormalizeDisplayText(string value) => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
}

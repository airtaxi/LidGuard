using System.ComponentModel;
using System.Diagnostics;
using LidGuard.Sessions;
using LidGuard.Hooks;

namespace LidGuard.Commands;

internal static class ManagedProviderCliResolver
{
    public static string GetProviderCliDisplayText(AgentProvider provider, bool hasProviderCli, string providerCliExecutablePath)
    {
        if (hasProviderCli && !string.IsNullOrWhiteSpace(providerCliExecutablePath)) return providerCliExecutablePath;
        return string.Join(" | ", GetProviderCliCandidatePaths(provider));
    }

    public static bool TryResolveProviderCliDisplayText(AgentProvider provider, out bool hasProviderCli, out string providerCliDisplayText)
    {
        hasProviderCli = TryResolveProviderCliExecutablePath(provider, out var providerCliExecutablePath, out _);
        providerCliDisplayText = GetProviderCliDisplayText(provider, hasProviderCli, providerCliExecutablePath);
        return hasProviderCli;
    }

    public static bool TryResolveProviderCliExecutablePath(AgentProvider provider, out string providerCliExecutablePath, out string message)
    {
        providerCliExecutablePath = string.Empty;
        message = string.Empty;

        foreach (var candidatePath in GetProviderCliCandidatePaths(provider))
        {
            if (!HookCommandUtilities.HookExecutableExists(candidatePath)) continue;

            providerCliExecutablePath = HookCommandUtilities.NormalizeHookExecutableReference(candidatePath);
            return true;
        }

        message =
            $"Provider CLI not found: {ManagedProviderSelection.GetProviderDisplayName(provider)} (checked: {string.Join(" | ", GetProviderCliCandidatePaths(provider))})";
        return false;
    }

    public static int RunProviderProcess(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            };

            foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start())
            {
                Console.Error.WriteLine($"Failed to start process: {fileName}");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IReadOnlyList<string> GetProviderCliCandidatePaths(AgentProvider provider)
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetLinksDirectoryPath = Path.Combine(localApplicationDataPath, "Microsoft", "WinGet", "Links");

        return provider switch
        {
            AgentProvider.Codex =>
            [
                "codex",
                Path.Combine(wingetLinksDirectoryPath, "codex.exe"),
                Path.Combine(localApplicationDataPath, "Programs", "OpenAI", "Codex", "bin", "codex.exe")
            ],
            AgentProvider.Claude =>
            [
                "claude",
                Path.Combine(wingetLinksDirectoryPath, "claude.exe")
            ],
            AgentProvider.GitHubCopilot =>
            [
                "copilot",
                Path.Combine(wingetLinksDirectoryPath, "copilot.exe")
            ],
            _ => []
        };
    }
}

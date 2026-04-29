using LidGuardLib.Commons.Sessions;

namespace LidGuard.Commands;

internal static class ManagedProviderSelection
{
    public static void ResolveAvailableProviders(
        IReadOnlyList<AgentProvider> selectedProviders,
        Func<AgentProvider, IReadOnlyList<string>> getProviderConfigurationRootCandidatePaths,
        out IReadOnlyList<AgentProvider> availableProviders,
        out IReadOnlyList<string> skippedProviderMessages)
    {
        availableProviders = selectedProviders;
        skippedProviderMessages = [];
        if (selectedProviders.Count < 2) return;

        var availableProviderList = new List<AgentProvider>();
        var skippedProviderMessageList = new List<string>();
        foreach (var provider in selectedProviders)
        {
            if (TryGetProviderAvailability(provider, getProviderConfigurationRootCandidatePaths(provider), out var skippedProviderMessage))
            {
                availableProviderList.Add(provider);
            }
            else
            {
                skippedProviderMessageList.Add(skippedProviderMessage);
            }
        }

        availableProviders = availableProviderList;
        skippedProviderMessages = skippedProviderMessageList;
    }

    public static bool TrySelectProviders(
        IReadOnlyDictionary<string, string> options,
        string prompt,
        out IReadOnlyList<AgentProvider> providers,
        out string message)
    {
        providers = [];
        message = string.Empty;

        var providerText = GetOption(options, "provider");
        return string.IsNullOrWhiteSpace(providerText)
            ? TryReadProviders(prompt, out providers, out message)
            : TryParseProviderSelection(providerText, out providers, out message);
    }

    public static string GetProviderDisplayName(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.Codex => "Codex",
            AgentProvider.Claude => "Claude",
            AgentProvider.GitHubCopilot => "GitHub Copilot",
            _ => provider.ToString()
        };
    }

    public static void WriteSkippedProviderMessages(IReadOnlyList<string> skippedProviderMessages)
    {
        if (skippedProviderMessages.Count == 0) return;

        foreach (var skippedProviderMessage in skippedProviderMessages) Console.WriteLine(skippedProviderMessage);
        Console.WriteLine();
    }

    public static int WriteNoAvailableProvidersFound()
    {
        Console.WriteLine("No available providers were found for all-provider execution.");
        return 0;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (options.TryGetValue(optionName, out var optionValue)) return optionValue;
        }

        return string.Empty;
    }

    private static bool HasExistingProviderConfigurationRoot(IReadOnlyList<string> candidatePaths)
    {
        foreach (var candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath)) continue;
            if (Directory.Exists(candidatePath)) return true;
            if (File.Exists(candidatePath)) return true;
        }

        return false;
    }

    private static bool TryGetProviderAvailability(AgentProvider provider, IReadOnlyList<string> candidatePaths, out string skippedProviderMessage)
    {
        skippedProviderMessage = string.Empty;
        if (HasExistingProviderConfigurationRoot(candidatePaths)) return true;

        skippedProviderMessage =
            $"Skipping absent provider: {GetProviderDisplayName(provider)} (no existing configuration root was found at: {string.Join(" | ", candidatePaths)})";
        return false;
    }

    private static bool TryParseProviderSelection(string providerText, out IReadOnlyList<AgentProvider> providers, out string message)
    {
        providers = [];
        message = string.Empty;

        providers = providerText.Trim().ToLowerInvariant() switch
        {
            "codex" => [AgentProvider.Codex],
            "claude" => [AgentProvider.Claude],
            "copilot" or "github-copilot" or "githubcopilot" => [AgentProvider.GitHubCopilot],
            "all" => [AgentProvider.Codex, AgentProvider.Claude, AgentProvider.GitHubCopilot],
            _ => []
        };

        if (providers.Count > 0) return true;

        message = "Unsupported provider. Use codex, claude, copilot, or all.";
        return false;
    }

    private static bool TryReadProviders(string prompt, out IReadOnlyList<AgentProvider> providers, out string message)
    {
        Console.Write($"{prompt} (codex, claude, copilot, all; default: all): ");
        var providerText = Console.ReadLine();
        if (providerText is null)
        {
            providers = [];
            message = "Input ended before a provider was selected.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(providerText)) providerText = "all";
        return TryParseProviderSelection(providerText, out providers, out message);
    }
}

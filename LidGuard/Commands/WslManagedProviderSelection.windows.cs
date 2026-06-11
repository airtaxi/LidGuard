using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class WslManagedProviderSelection
{
    public static void ResolveAvailableProviders(string distroName, IReadOnlyList<AgentProvider> selectedProviders, Func<string, AgentProvider, bool> tryGetProviderAvailability, out IReadOnlyList<AgentProvider> availableProviders, out IReadOnlyList<string> skippedProviderMessages)
    {
        availableProviders = selectedProviders;
        skippedProviderMessages = [];
        if (selectedProviders.Count < 2) return;

        var availableProviderList = new List<AgentProvider>();
        var skippedProviderMessageList = new List<string>();
        foreach (var provider in selectedProviders)
        {
            if (tryGetProviderAvailability(distroName, provider)) availableProviderList.Add(provider);
            else skippedProviderMessageList.Add(LocalizationService.GetFormattedString("ManagementSkippedAbsentProvider", ManagedProviderSelection.GetProviderDisplayName(provider), "WSL"));
        }

        availableProviders = availableProviderList;
        skippedProviderMessages = skippedProviderMessageList;
    }

    public static bool TryHasHookProviderConfigurationRoot(string distroName, AgentProvider provider)
    {
        if (!WslProviderConfigurationRoots.TryGetHookCandidatePaths(distroName, provider, out var candidatePaths, out _)) return false;
        return HasExistingPath(distroName, candidatePaths);
    }

    public static bool TryHasMcpProviderConfigurationRoot(string distroName, AgentProvider provider)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpCandidatePaths(distroName, provider, out var candidatePaths, out _)) return false;
        return HasExistingPath(distroName, candidatePaths);
    }

    private static bool HasExistingPath(string distroName, IReadOnlyList<string> candidatePaths)
    {
        foreach (var candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath)) continue;
            if (WslCommandUtilities.PathExists(distroName, candidatePath)) return true;
        }

        return false;
    }
}

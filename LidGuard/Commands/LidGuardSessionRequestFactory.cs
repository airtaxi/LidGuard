using LidGuard.Ipc;
using LidGuard.Settings;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Commands;

internal static class LidGuardSessionRequestFactory
{
    public static bool TryCreateSessionRequest(
        IReadOnlyDictionary<string, string> options,
        string commandName,
        bool includeSettings,
        out LidGuardPipeRequest request,
        out string message)
    {
        request = new LidGuardPipeRequest();
        message = string.Empty;

        var settings = LidGuardSettings.Default;
        if (includeSettings && !LidGuardSettingsStore.TryLoadOrCreate(out settings, out message)) return false;

        var providerText = CommandOptionReader.GetOption(options, "provider");
        if (!AgentProviderOptionParser.TryParseProvider(providerText, out var provider))
        {
            message = "A provider is required. Use codex, claude, copilot, custom, mcp, or unknown.";
            return false;
        }

        var workingDirectory = GetWorkingDirectory(options);
        var providerName = AgentProviderOptionParser.GetSessionProviderName(options, provider);
        var sessionIdentifier = CommandOptionReader.GetOption(options, "session", "session-id", "session-identifier");
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) sessionIdentifier = CreateFallbackSessionIdentifier(provider, providerName, workingDirectory);
        if (provider == AgentProvider.Mcp && string.IsNullOrWhiteSpace(providerName))
        {
            message = "The --provider-name option is required when --provider mcp is used.";
            return false;
        }

        if (!TryParseWatchedProcessIdentifier(options, out var watchedProcessIdentifier, out message)) return false;

        request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = provider,
            ProviderName = providerName,
            SessionIdentifier = sessionIdentifier,
            WatchedProcessIdentifier = watchedProcessIdentifier,
            WorkingDirectory = workingDirectory,
            HasSettings = includeSettings,
            Settings = settings
        };

        return true;
    }

    public static bool TryCreateSessionRemovalRequest(
        IReadOnlyDictionary<string, string> options,
        out LidGuardPipeRequest request,
        out string message)
    {
        request = new LidGuardPipeRequest();
        message = string.Empty;

        if (!CommandOptionReader.TryParseBooleanOption(options, false, out var removeAllSessions, out message, "all")) return false;
        if (removeAllSessions)
        {
            if (CommandOptionReader.TryGetOption(options, out _, "session", "session-id", "session-identifier"))
            {
                message = "The --all option cannot be combined with --session.";
                return false;
            }

            if (CommandOptionReader.TryGetOption(options, out _, "provider"))
            {
                message = "The --all option cannot be combined with --provider.";
                return false;
            }

            if (CommandOptionReader.TryGetOption(options, out _, "provider-name"))
            {
                message = "The --all option cannot be combined with --provider-name.";
                return false;
            }

            request = new LidGuardPipeRequest
            {
                Command = LidGuardPipeCommands.RemoveSession,
                MatchAllSessions = true
            };
            return true;
        }

        var sessionIdentifier = CommandOptionReader.GetOption(options, "session", "session-id", "session-identifier");
        if (string.IsNullOrWhiteSpace(sessionIdentifier))
        {
            message = "A session identifier is required.";
            return false;
        }

        var provider = AgentProvider.Unknown;
        var providerWasSpecified = CommandOptionReader.TryGetOption(options, out var providerText, "provider");
        if (providerWasSpecified && !AgentProviderOptionParser.TryParseProvider(providerText, out provider))
        {
            message = "Unsupported provider. Use codex, claude, copilot, custom, mcp, or unknown.";
            return false;
        }

        var providerName = providerWasSpecified ? AgentProviderOptionParser.GetSessionProviderName(options, provider) : string.Empty;

        request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.RemoveSession,
            Provider = provider,
            ProviderName = providerName,
            SessionIdentifier = sessionIdentifier,
            MatchAllProvidersForSessionIdentifier = !providerWasSpecified,
            MatchAllProviderNamesForSessionIdentifier = provider == AgentProvider.Mcp && string.IsNullOrWhiteSpace(providerName)
        };
        return true;
    }

    private static bool TryParseWatchedProcessIdentifier(
        IReadOnlyDictionary<string, string> options,
        out int watchedProcessIdentifier,
        out string message)
    {
        watchedProcessIdentifier = 0;
        message = string.Empty;

        var watchedProcessText = CommandOptionReader.GetOption(options, "parent-pid", "watched-process-id", "watched-process-identifier");
        if (string.IsNullOrWhiteSpace(watchedProcessText)) return true;
        if (int.TryParse(watchedProcessText, out watchedProcessIdentifier) && watchedProcessIdentifier >= 0) return true;

        message = "The watched process identifier must be a non-negative integer.";
        return false;
    }

    private static string GetWorkingDirectory(IReadOnlyDictionary<string, string> options)
    {
        var workingDirectory = CommandOptionReader.GetOption(options, "working-directory", "cwd");
        return string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
    }

    private static string CreateFallbackSessionIdentifier(AgentProvider provider, string providerName, string workingDirectory)
    {
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(provider, providerName);
        return $"{providerDisplayText}:{normalizedWorkingDirectory}";
    }

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory; }
    }
}

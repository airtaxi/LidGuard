using System.Diagnostics;
using LidGuard.Control;
using LidGuard.Hooks;
using LidGuard.Ipc;
using LidGuard.Mcp;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuardLib.Commons.Platform;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;
using LidGuardLib.Windows.Platform;

namespace LidGuard.Commands;

internal static class LidGuardCommandLineApplication
{
    private static readonly string[] s_supportedPostStopSuspendSystemSoundNames = ["Asterisk", "Beep", "Exclamation", "Hand", "Question"];

    public static async Task<int> RunAsync(string[] commandLineArguments)
    {
        var runtimePlatform = new WindowsLidGuardRuntimePlatform();
        if (!runtimePlatform.IsSupported) return WriteUnsupportedPlatform(runtimePlatform);

        if (commandLineArguments.Length == 0) return WriteHelp(1);

        var commandName = commandLineArguments[0].Trim().ToLowerInvariant();
        if (commandName is "help" or "--help" or "-h" or "/?") return WriteHelp(0);
        if (!TryRestorePendingLidActionBackup(runtimePlatform, out var recoveryMessage))
        {
            Console.Error.WriteLine(recoveryMessage);
            return 1;
        }

        if (commandName == LidGuardPipeCommands.ClaudeHook) return await ClaudeHookCommand.RunAsync();
        if (commandName == LidGuardPipeCommands.CopilotHook) return await GitHubCopilotHookCommand.RunAsync(commandLineArguments[1..]);
        if (commandName == LidGuardPipeCommands.CodexHook) return await CodexHookCommand.RunAsync();
        if (commandName == LidGuardMcpServerCommand.CommandName) return await LidGuardMcpServerCommand.RunAsync(commandLineArguments[1..]);
        if (commandName == LidGuardPipeCommands.RunServer) return await RunServerAsync(runtimePlatform);

        if (!TryParseOptions(commandLineArguments, 1, out var options, out var parseMessage))
        {
            Console.Error.WriteLine(parseMessage);
            return 1;
        }

        return commandName switch
        {
            LidGuardPipeCommands.Start => await SendStartAsync(options),
            LidGuardPipeCommands.Stop => await SendStopAsync(options),
            LidGuardPipeCommands.RemovePreSuspendWebhook => await SendRemovePreSuspendWebhookAsync(options, runtimePlatform),
            LidGuardPipeCommands.RemoveSession => await SendRemoveSessionAsync(options),
            LidGuardPipeCommands.Status => await SendStatusAsync(),
            LidGuardPipeCommands.CleanupOrphans => await SendCleanupOrphansAsync(),
            LidGuardPipeCommands.Settings => await SendSettingsAsync(options, runtimePlatform),
            LidGuardPipeCommands.PreviewSystemSound => await PreviewSystemSoundAsync(options, runtimePlatform),
            LidGuardPipeCommands.ClaudeHooks => ClaudeHookCommand.WriteHookSnippet(options),
            LidGuardPipeCommands.CopilotHooks => GitHubCopilotHookCommand.WriteHookSnippet(options),
            LidGuardPipeCommands.CodexHooks => CodexHookCommand.WriteHookSnippet(options),
            LidGuardPipeCommands.HookStatus => HookManagementCommand.WriteHookStatus(options),
            LidGuardPipeCommands.HookInstall => HookManagementCommand.InstallHook(options),
            LidGuardPipeCommands.HookRemove or "hook-uninstall" => HookManagementCommand.RemoveHook(options),
            LidGuardPipeCommands.HookEvents => HookManagementCommand.WriteHookEvents(options),
            LidGuardPipeCommands.McpStatus => McpManagementCommand.WriteMcpStatus(options),
            LidGuardPipeCommands.McpInstall => McpManagementCommand.InstallMcp(options),
            LidGuardPipeCommands.McpRemove or "mcp-uninstall" => McpManagementCommand.RemoveMcp(options),
            _ => WriteUnknownCommand(commandName)
        };
    }

    private static async Task<int> RunServerAsync(ILidGuardRuntimePlatform runtimePlatform)
    {
        using var runtimeMutex = new Mutex(false, LidGuardPipeNames.RuntimeMutexName);
        if (!TryAcquireRuntimeMutex(runtimeMutex)) return 0;

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArguments) =>
            {
                eventArguments.Cancel = true;
                cancellationTokenSource.Cancel();
            };

            var serviceSetResult = runtimePlatform.CreateRuntimeServiceSet();
            if (!serviceSetResult.Succeeded)
            {
                Console.WriteLine(serviceSetResult.Message);
                return 0;
            }

            using var serviceSet = serviceSetResult.Value;
            var settings = CreateRuntimeSettings();
            var runtimeCoordinator = new LidGuardRuntimeCoordinator(
                settings,
                serviceSet.PowerRequestService,
                serviceSet.CommandLineProcessResolver,
                serviceSet.ProcessExitWatcher,
                serviceSet.LidActionPolicyController,
                serviceSet.SystemSuspendService,
                serviceSet.PostStopSuspendSoundPlayer,
                serviceSet.LidStateSource);

            var pipeServer = new LidGuardPipeServer(runtimeCoordinator);

            try
            {
                await pipeServer.RunAsync(cancellationTokenSource.Token);
                return 0;
            }
            catch (OperationCanceledException) { return 0; }
        }
        finally
        {
            runtimeMutex.ReleaseMutex();
        }
    }

    private static bool TryAcquireRuntimeMutex(Mutex runtimeMutex)
    {
        try { return runtimeMutex.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    private static bool TryRestorePendingLidActionBackup(ILidGuardRuntimePlatform runtimePlatform, out string message)
    {
        using var runtimeMutex = new Mutex(false, LidGuardPipeNames.RuntimeMutexName);
        var ownsRuntimeMutex = false;
        message = string.Empty;

        try
        {
            try
            {
                if (!runtimeMutex.WaitOne(0)) return true;
                ownsRuntimeMutex = true;
            }
            catch (AbandonedMutexException)
            {
                ownsRuntimeMutex = true;
            }

            if (!File.Exists(LidGuardPendingLidActionBackupStore.GetDefaultFilePath())) return true;

            var serviceSetResult = runtimePlatform.CreateRuntimeServiceSet();
            if (!serviceSetResult.Succeeded)
            {
                message = serviceSetResult.Message;
                return false;
            }

            using var serviceSet = serviceSetResult.Value;
            var pendingBackupManager = new LidGuardPendingLidActionBackupManager(serviceSet.LidActionPolicyController);
            var restoreResult = pendingBackupManager.RestorePendingBackupIfPresent();
            if (restoreResult.Succeeded) return true;

            message = restoreResult.Message;
            return false;
        }
        finally
        {
            if (ownsRuntimeMutex) runtimeMutex.ReleaseMutex();
        }
    }

    private static int WriteUnsupportedPlatform(ILidGuardRuntimePlatform runtimePlatform)
    {
        Console.WriteLine(runtimePlatform.UnsupportedMessage);
        return 0;
    }

    private static LidGuardSettings CreateRuntimeSettings()
    {
        if (LidGuardSettingsStore.TryLoadOrCreate(out var settings, out _)) return settings;
        return LidGuardSettings.HeadlessRuntimeDefault;
    }

    private static async Task<int> SendStartAsync(IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateSessionRequest(options, LidGuardPipeCommands.Start, true, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var response = await new LidGuardRuntimeClient().SendAsync(request, true);
        return WriteResponse(response);
    }

    private static async Task<int> SendStopAsync(IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateSessionRequest(options, LidGuardPipeCommands.Stop, false, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        return WriteResponse(response);
    }

    private static async Task<int> SendRemoveSessionAsync(IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateSessionRemovalRequest(options, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        if (!response.Succeeded && response.RuntimeUnavailable)
        {
            Console.WriteLine("LidGuard runtime is not running. No active session was removed.");
            return 0;
        }

        return WriteResponse(response);
    }

    private static async Task<int> SendStatusAsync()
    {
        var request = new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status };
        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        if (!response.Succeeded && response.RuntimeUnavailable)
        {
            Console.WriteLine("LidGuard runtime is not running.");
            Console.WriteLine("Active sessions: 0");
            if (LidGuardSettingsStore.TryLoadOrCreate(out var settings, out var settingsMessage))
            {
                Console.WriteLine($"Settings file: {LidGuardSettingsStore.GetDefaultSettingsFilePath()}");
                WriteSettings(settings);
            }
            else
            {
                Console.Error.WriteLine(settingsMessage);
            }

            return 0;
        }

        return WriteResponse(response, true, true);
    }

    private static async Task<int> SendCleanupOrphansAsync()
    {
        var request = new LidGuardPipeRequest { Command = LidGuardPipeCommands.CleanupOrphans };
        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        if (!response.Succeeded && response.RuntimeUnavailable)
        {
            Console.WriteLine("LidGuard runtime is not running. Nothing to clean up.");
            return 0;
        }

        return WriteResponse(response);
    }

    private static async Task<int> SendSettingsAsync(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform)
    {
        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var loadMessage))
        {
            Console.Error.WriteLine(loadMessage);
            return 1;
        }

        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        var settings = LidGuardSettings.Default;
        var settingsMessage = string.Empty;
        var isInteractiveSettings = options.Count == 0;
        var settingsCreated = isInteractiveSettings
            ? TryCreateInteractiveSettings(currentSettings, out settings, out settingsMessage)
            : TryCreateSettings(options, currentSettings, out settings, out settingsMessage);

        if (!settingsCreated)
        {
            Console.Error.WriteLine(settingsMessage);
            return 1;
        }

        if (!PostStopSuspendSoundConfiguration.TryNormalize(
            settings,
            postStopSuspendSoundPlayerResult.Value,
            out settings,
            out settingsMessage))
        {
            Console.Error.WriteLine(settingsMessage);
            return 1;
        }
        if (!LidGuardSettingsStore.TrySave(settings, out var saveMessage))
        {
            Console.Error.WriteLine(saveMessage);
            return 1;
        }

        var request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.Settings,
            HasSettings = true,
            Settings = settings
        };

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        Console.WriteLine($"Settings file: {LidGuardSettingsStore.GetDefaultSettingsFilePath()}");
        WriteSettings(settings);
        if (isInteractiveSettings)
        {
            Console.WriteLine($"To change Reason, run: {GetCommandDisplayName()} settings --power-request-reason <text>");
            Console.WriteLine($"To change Pre-suspend webhook URL, run: {GetCommandDisplayName()} settings --pre-suspend-webhook-url <http-or-https-url>");
            Console.WriteLine($"To remove Pre-suspend webhook URL, run: {GetCommandDisplayName()} {LidGuardPipeCommands.RemovePreSuspendWebhook}");
        }

        if (response.Succeeded)
        {
            Console.WriteLine("Runtime settings updated.");
            return 0;
        }

        if (response.RuntimeUnavailable)
        {
            Console.WriteLine("Runtime is not running; saved settings will be used on the next start.");
            return 0;
        }

        Console.Error.WriteLine(response.Message);
        return 1;
    }

    private static async Task<int> SendRemovePreSuspendWebhookAsync(
        IReadOnlyDictionary<string, string> options,
        ILidGuardRuntimePlatform runtimePlatform)
    {
        if (options.Count > 0)
        {
            Console.Error.WriteLine($"{LidGuardPipeCommands.RemovePreSuspendWebhook} does not accept options.");
            return 1;
        }

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var loadMessage))
        {
            Console.Error.WriteLine(loadMessage);
            return 1;
        }

        var normalizedCurrentSettings = LidGuardSettings.Normalize(currentSettings);
        if (string.IsNullOrWhiteSpace(normalizedCurrentSettings.PreSuspendWebhookUrl))
        {
            Console.WriteLine("No pre-suspend webhook URL is configured.");
            return 0;
        }

        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        var controlService = new LidGuardControlService(postStopSuspendSoundPlayerResult.Value);
        var updateResult = await controlService.UpdateSettingsAsync(
            new LidGuardSettingsPatch { PreSuspendWebhookUrl = string.Empty });
        if (!updateResult.Succeeded)
        {
            Console.Error.WriteLine(updateResult.Message);
            return 1;
        }

        var outcome = updateResult.Value;
        Console.WriteLine($"Settings file: {LidGuardSettingsStore.GetDefaultSettingsFilePath()}");
        WriteSettings(outcome.UpdatedStoredSettings);
        Console.WriteLine("Pre-suspend webhook URL removed.");

        if (outcome.Snapshot.RuntimeReachable)
        {
            Console.WriteLine("Runtime settings updated.");
            return 0;
        }

        if (outcome.Snapshot.RuntimeUnavailable)
        {
            Console.WriteLine("Runtime is not running; saved settings will be used on the next start.");
            return 0;
        }

        Console.Error.WriteLine(outcome.Snapshot.RuntimeMessage);
        return 1;
    }

    private static async Task<int> PreviewSystemSoundAsync(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform)
    {
        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        var systemSoundName = GetOption(options, "name", "system-sound");
        if (string.IsNullOrWhiteSpace(systemSoundName))
        {
            Console.Error.WriteLine($"A system sound name is required. Supported values: {DescribeSupportedPostStopSuspendSystemSounds()}");
            return 1;
        }

        var normalizedSystemSoundName = systemSoundName.Trim();
        if (!s_supportedPostStopSuspendSystemSoundNames.Any(
            supportedSystemSoundName => supportedSystemSoundName.Equals(normalizedSystemSoundName, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Unsupported system sound name: {normalizedSystemSoundName}");
            Console.Error.WriteLine($"Supported values: {DescribeSupportedPostStopSuspendSystemSounds()}");
            return 1;
        }

        var playbackResult = await postStopSuspendSoundPlayerResult.Value.PlayAsync(normalizedSystemSoundName, CancellationToken.None);
        if (!playbackResult.Succeeded)
        {
            Console.Error.WriteLine(playbackResult.Message);
            return 1;
        }

        Console.WriteLine($"Played system sound: {normalizedSystemSoundName}");
        return 0;
    }

    private static bool TryCreateSessionRequest(
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

        var providerText = GetOption(options, "provider");
        if (!TryParseProvider(providerText, out var provider))
        {
            message = "A provider is required. Use codex, claude, copilot, custom, or unknown.";
            return false;
        }

        var workingDirectory = GetWorkingDirectory(options);
        var sessionIdentifier = GetOption(options, "session", "session-id", "session-identifier");
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) sessionIdentifier = CreateFallbackSessionIdentifier(provider, workingDirectory);

        if (!TryParseWatchedProcessIdentifier(options, out var watchedProcessIdentifier, out message)) return false;

        request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = provider,
            SessionIdentifier = sessionIdentifier,
            WatchedProcessIdentifier = watchedProcessIdentifier,
            WorkingDirectory = workingDirectory,
            HasSettings = includeSettings,
            Settings = settings
        };

        return true;
    }

    private static bool TryCreateSessionRemovalRequest(
        IReadOnlyDictionary<string, string> options,
        out LidGuardPipeRequest request,
        out string message)
    {
        request = new LidGuardPipeRequest();
        message = string.Empty;

        var sessionIdentifier = GetOption(options, "session", "session-id", "session-identifier");
        if (string.IsNullOrWhiteSpace(sessionIdentifier))
        {
            message = "A session identifier is required.";
            return false;
        }

        var provider = AgentProvider.Unknown;
        var providerWasSpecified = TryGetOption(options, out var providerText, "provider");
        if (providerWasSpecified && !TryParseProvider(providerText, out provider))
        {
            message = "Unsupported provider. Use codex, claude, copilot, custom, or unknown.";
            return false;
        }

        request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.RemoveSession,
            Provider = provider,
            SessionIdentifier = sessionIdentifier,
            MatchAllProvidersForSessionIdentifier = !providerWasSpecified
        };
        return true;
    }

    private static bool TryParseOptions(
        string[] commandLineArguments,
        int firstOptionIndex,
        out Dictionary<string, string> options,
        out string message)
    {
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        message = string.Empty;

        for (var argumentIndex = firstOptionIndex; argumentIndex < commandLineArguments.Length; argumentIndex++)
        {
            var argument = commandLineArguments[argumentIndex];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                message = $"Unexpected argument: {argument}";
                return false;
            }

            var separatorIndex = argument.IndexOf('=');
            if (separatorIndex > 2)
            {
                var optionName = argument[2..separatorIndex];
                options[optionName] = argument[(separatorIndex + 1)..];
                continue;
            }

            var standaloneOptionName = argument[2..];
            if (string.IsNullOrWhiteSpace(standaloneOptionName))
            {
                message = "An option name is required after --.";
                return false;
            }

            if (argumentIndex + 1 >= commandLineArguments.Length || commandLineArguments[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[standaloneOptionName] = bool.TrueString;
                continue;
            }

            options[standaloneOptionName] = commandLineArguments[++argumentIndex];
        }

        return true;
    }

    private static bool TryParseProvider(string providerText, out AgentProvider provider)
    {
        provider = AgentProvider.Unknown;
        if (string.IsNullOrWhiteSpace(providerText)) return false;

        provider = providerText.Trim().ToLowerInvariant() switch
        {
            "codex" => AgentProvider.Codex,
            "claude" => AgentProvider.Claude,
            "copilot" or "github-copilot" or "githubcopilot" => AgentProvider.GitHubCopilot,
            "custom" => AgentProvider.Custom,
            "unknown" => AgentProvider.Unknown,
            _ => AgentProvider.Unknown
        };

        return provider != AgentProvider.Unknown || providerText.Equals("unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseWatchedProcessIdentifier(
        IReadOnlyDictionary<string, string> options,
        out int watchedProcessIdentifier,
        out string message)
    {
        watchedProcessIdentifier = 0;
        message = string.Empty;

        var watchedProcessText = GetOption(options, "parent-pid", "watched-process-id", "watched-process-identifier");
        if (string.IsNullOrWhiteSpace(watchedProcessText)) return true;
        if (int.TryParse(watchedProcessText, out watchedProcessIdentifier) && watchedProcessIdentifier >= 0) return true;

        message = "The watched process identifier must be a non-negative integer.";
        return false;
    }

    private static bool TryCreateSettings(
        IReadOnlyDictionary<string, string> options,
        LidGuardSettings currentSettings,
        out LidGuardSettings settings,
        out string message)
    {
        settings = LidGuardSettings.Normalize(currentSettings);
        if (!TryParseBooleanOption(options, false, out var resetSettings, out message, "reset", "default", "defaults")) return false;

        var baseSettings = resetSettings ? LidGuardSettings.HeadlessRuntimeDefault : settings;
        var basePowerRequest = baseSettings.PowerRequest ?? PowerRequestOptions.Default;
        settings = baseSettings;
        message = string.Empty;

        if (!TryParseBooleanOption(options, basePowerRequest.PreventSystemSleep, out var preventSystemSleep, out message, "prevent-system-sleep", "system-required")) return false;
        if (!TryParseBooleanOption(options, basePowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message, "prevent-away-mode-sleep", "away-mode-required")) return false;
        if (!TryParseBooleanOption(options, basePowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message, "prevent-display-sleep", "display-required")) return false;
        if (!TryParseBooleanOption(options, baseSettings.ChangeLidAction, out var changeLidAction, out message, "change-lid-action", "lid-action")) return false;
        if (!TryParseBooleanOption(options, baseSettings.WatchParentProcess, out var watchParentProcess, out message, "watch-parent-process", "watch-parent")) return false;
        if (!TryParseSuspendModeOption(options, baseSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!TryParsePostStopSuspendDelaySecondsOption(options, baseSettings.PostStopSuspendDelaySeconds, out var postStopSuspendDelaySeconds, out message)) return false;
        var postStopSuspendSound = baseSettings.PostStopSuspendSound;
        if (TryGetOption(options, out var postStopSuspendSoundText, "post-stop-suspend-sound")) postStopSuspendSound = postStopSuspendSoundText;
        if (!TryParsePreSuspendWebhookUrlOption(options, baseSettings.PreSuspendWebhookUrl, out var preSuspendWebhookUrl, out message)) return false;
        if (!TryParseClosedLidPermissionRequestDecisionOption(options, baseSettings.ClosedLidPermissionRequestDecision, out var closedLidPermissionRequestDecision, out message)) return false;

        var reason = GetOption(options, "power-request-reason", "reason");
        if (string.IsNullOrWhiteSpace(reason)) reason = basePowerRequest.Reason;

        settings = new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = preventSystemSleep,
                PreventAwayModeSleep = preventAwayModeSleep,
                PreventDisplaySleep = preventDisplaySleep,
                Reason = reason
            },
            ChangeLidAction = changeLidAction,
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
            PostStopSuspendSound = postStopSuspendSound,
            PreSuspendWebhookUrl = preSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = closedLidPermissionRequestDecision,
            WatchParentProcess = watchParentProcess
        };

        return true;
    }

    private static bool TryCreateInteractiveSettings(LidGuardSettings currentSettings, out LidGuardSettings settings, out string message)
    {
        var normalizedStoredSettings = LidGuardSettings.Normalize(currentSettings);
        var storedPowerRequest = normalizedStoredSettings.PowerRequest ?? PowerRequestOptions.Default;
        var defaultSettings = LidGuardSettings.Normalize(LidGuardSettings.HeadlessRuntimeDefault);
        var defaultPowerRequest = defaultSettings.PowerRequest ?? PowerRequestOptions.Default;
        settings = normalizedStoredSettings;
        message = string.Empty;

        if (!TryReadBooleanSetting("Prevent system sleep", storedPowerRequest.PreventSystemSleep, defaultPowerRequest.PreventSystemSleep, out var preventSystemSleep, out message)) return false;
        if (!TryReadBooleanSetting("Prevent away mode sleep", storedPowerRequest.PreventAwayModeSleep, defaultPowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message)) return false;
        if (!TryReadBooleanSetting("Prevent display sleep", storedPowerRequest.PreventDisplaySleep, defaultPowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message)) return false;
        if (!TryReadBooleanSetting("Change lid action", normalizedStoredSettings.ChangeLidAction, defaultSettings.ChangeLidAction, out var changeLidAction, out message)) return false;
        if (!TryReadBooleanSetting("Watch parent process", normalizedStoredSettings.WatchParentProcess, defaultSettings.WatchParentProcess, out var watchParentProcess, out message)) return false;
        if (!TryReadSuspendModeSetting("Suspend mode", normalizedStoredSettings.SuspendMode, defaultSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!TryReadNonNegativeIntegerSetting(
            "Post-stop suspend delay seconds",
            normalizedStoredSettings.PostStopSuspendDelaySeconds,
            defaultSettings.PostStopSuspendDelaySeconds,
            out var postStopSuspendDelaySeconds,
            out message))
            return false;
        if (!TryReadPostStopSuspendSoundSetting(
            "Post-stop suspend sound",
            normalizedStoredSettings.PostStopSuspendSound,
            defaultSettings.PostStopSuspendSound,
            out var postStopSuspendSound,
            out message))
            return false;
        if (!TryReadClosedLidPermissionRequestDecisionSetting(
            "Closed lid permission request decision",
            normalizedStoredSettings.ClosedLidPermissionRequestDecision,
            defaultSettings.ClosedLidPermissionRequestDecision,
            out var closedLidPermissionRequestDecision,
            out message))
            return false;

        settings = new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = preventSystemSleep,
                PreventAwayModeSleep = preventAwayModeSleep,
                PreventDisplaySleep = preventDisplaySleep,
                Reason = storedPowerRequest.Reason
            },
            ChangeLidAction = changeLidAction,
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
            PostStopSuspendSound = postStopSuspendSound,
            PreSuspendWebhookUrl = normalizedStoredSettings.PreSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = closedLidPermissionRequestDecision,
            WatchParentProcess = watchParentProcess
        };

        return true;
    }

    private static bool TryReadBooleanSetting(string settingName, bool storedValue, bool defaultValue, out bool value, out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString());

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (TryParseInteractiveBoolean(valueText.Trim(), out value)) return true;

        message = $"{settingName} must be true or false.";
        return false;
    }

    private static bool TryReadSuspendModeSetting(
        string settingName,
        SystemSuspendMode storedValue,
        SystemSuspendMode defaultValue,
        out SystemSuspendMode value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Sleep, Hibernate");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;

        var normalizedValueText = valueText.Trim();
        value = normalizedValueText.ToLowerInvariant() switch
        {
            "sleep" => SystemSuspendMode.Sleep,
            "hibernate" => SystemSuspendMode.Hibernate,
            _ => storedValue
        };

        if (normalizedValueText.Equals("sleep", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalizedValueText.Equals("hibernate", StringComparison.OrdinalIgnoreCase)) return true;

        message = $"{settingName} must be sleep or hibernate.";
        return false;
    }

    private static bool TryReadNonNegativeIntegerSetting(
        string settingName,
        int storedValue,
        int defaultValue,
        out int value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "0 = immediate");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value) && value >= 0) return true;

        message = $"{settingName} must be a non-negative integer.";
        return false;
    }

    private static bool TryReadPostStopSuspendSoundSetting(
        string settingName,
        string storedValue,
        string defaultValue,
        out string value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        var storedDisplayValue = PostStopSuspendSoundConfiguration.GetDisplayValue(storedValue);
        var defaultDisplayValue = PostStopSuspendSoundConfiguration.GetDisplayValue(defaultValue);
        WriteInteractiveSettingPrompt(
            settingName,
            storedDisplayValue,
            defaultDisplayValue,
            $"use off to disable, SystemSounds: {DescribeSupportedPostStopSuspendSystemSounds()}, or a .wav path");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        value = valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase) ? string.Empty : valueText.Trim();
        return true;
    }

    private static bool TryParsePreSuspendWebhookUrlOption(
        IReadOnlyDictionary<string, string> options,
        string defaultValue,
        out string preSuspendWebhookUrl,
        out string message)
    {
        preSuspendWebhookUrl = defaultValue;
        message = string.Empty;
        if (!TryGetOption(options, out var preSuspendWebhookUrlText, "pre-suspend-webhook-url", "suspend-webhook-url")) return true;

        if (string.IsNullOrWhiteSpace(preSuspendWebhookUrlText) || preSuspendWebhookUrlText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            message = $"Use {GetCommandDisplayName()} {LidGuardPipeCommands.RemovePreSuspendWebhook} to remove the pre-suspend webhook URL.";
            return false;
        }

        return PreSuspendWebhookConfiguration.TryNormalizeConfiguredValue(
            preSuspendWebhookUrlText,
            out preSuspendWebhookUrl,
            out message);
    }

    private static bool TryReadClosedLidPermissionRequestDecisionSetting(
        string settingName,
        ClosedLidPermissionRequestDecision storedValue,
        ClosedLidPermissionRequestDecision defaultValue,
        out ClosedLidPermissionRequestDecision value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Deny, Allow");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        return TryParseClosedLidPermissionRequestDecision(valueText, out value, out message);
    }

    private static void WriteInteractiveSettingPrompt(
        string settingName,
        string storedValueText,
        string defaultValueText,
        string additionalDetails = "")
    {
        var prompt = $"{settingName} (stored: {storedValueText}, default: {defaultValueText}";
        if (!string.IsNullOrWhiteSpace(additionalDetails)) prompt = $"{prompt}, {additionalDetails}";
        prompt = $"{prompt}, press Enter to keep stored): ";
        Console.Write(prompt);
    }

    private static bool TryParseInteractiveBoolean(string valueText, out bool value)
    {
        value = false;
        if (valueText.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (valueText.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryParseBooleanOption(
        IReadOnlyDictionary<string, string> options,
        bool defaultValue,
        out bool value,
        out string message,
        params string[] optionNames)
    {
        value = defaultValue;
        message = string.Empty;
        if (!TryGetOption(options, out var valueText, optionNames)) return true;
        if (TryParseBoolean(valueText, out value)) return true;

        message = $"The {optionNames[0]} option must be true or false.";
        return false;
    }

    private static bool TryParseBoolean(string valueText, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(valueText)) return false;

        switch (valueText.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "y":
            case "on":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
            case "off":
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseClosedLidPermissionRequestDecisionOption(
        IReadOnlyDictionary<string, string> options,
        ClosedLidPermissionRequestDecision defaultValue,
        out ClosedLidPermissionRequestDecision closedLidPermissionRequestDecision,
        out string message)
    {
        closedLidPermissionRequestDecision = defaultValue;
        message = string.Empty;
        if (!TryGetOption(options, out var permissionRequestDecisionText, "closed-lid-permission-request-decision", "permission-request-decision-when-lid-closed")) return true;
        return TryParseClosedLidPermissionRequestDecision(permissionRequestDecisionText, out closedLidPermissionRequestDecision, out message);
    }

    private static bool TryParseClosedLidPermissionRequestDecision(
        string permissionRequestDecisionText,
        out ClosedLidPermissionRequestDecision closedLidPermissionRequestDecision,
        out string message)
    {
        closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Deny;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(permissionRequestDecisionText))
        {
            message = "Closed lid permission request decision must be deny or allow.";
            return false;
        }

        switch (permissionRequestDecisionText.Trim().ToLowerInvariant())
        {
            case "allow":
                closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Allow;
                return true;
            case "deny":
                closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Deny;
                return true;
            default:
                message = "Closed lid permission request decision must be deny or allow.";
                return false;
        }
    }

    private static bool TryParseSuspendModeOption(
        IReadOnlyDictionary<string, string> options,
        SystemSuspendMode defaultValue,
        out SystemSuspendMode suspendMode,
        out string message)
    {
        suspendMode = defaultValue;
        message = string.Empty;
        if (!TryGetOption(options, out var suspendModeText, "suspend-mode")) return true;

        var normalizedSuspendModeText = suspendModeText.Trim();
        suspendMode = normalizedSuspendModeText.ToLowerInvariant() switch
        {
            "sleep" => SystemSuspendMode.Sleep,
            "hibernate" => SystemSuspendMode.Hibernate,
            _ => defaultValue
        };

        if (suspendMode == defaultValue && !normalizedSuspendModeText.Equals(defaultValue.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            message = "The suspend-mode option must be sleep or hibernate.";
            return false;
        }

        return true;
    }

    private static bool TryParsePostStopSuspendDelaySecondsOption(
        IReadOnlyDictionary<string, string> options,
        int defaultValue,
        out int postStopSuspendDelaySeconds,
        out string message)
    {
        postStopSuspendDelaySeconds = defaultValue;
        message = string.Empty;
        if (!TryGetOption(options, out var postStopSuspendDelaySecondsText, "post-stop-suspend-delay-seconds")) return true;
        if (int.TryParse(postStopSuspendDelaySecondsText.Trim(), out postStopSuspendDelaySeconds) && postStopSuspendDelaySeconds >= 0) return true;

        message = "The post-stop-suspend-delay-seconds option must be a non-negative integer.";
        return false;
    }

    private static string GetWorkingDirectory(IReadOnlyDictionary<string, string> options)
    {
        var workingDirectory = GetOption(options, "working-directory", "cwd");
        return string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, params string[] optionNames)
    {
        return TryGetOption(options, out var optionValue, optionNames) ? optionValue : string.Empty;
    }

    private static bool TryGetOption(IReadOnlyDictionary<string, string> options, out string optionValue, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (options.TryGetValue(optionName, out optionValue)) return true;
        }

        optionValue = string.Empty;
        return false;
    }

    private static string CreateFallbackSessionIdentifier(AgentProvider provider, string workingDirectory)
    {
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        return $"{provider}:{normalizedWorkingDirectory}";
    }

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory; }
    }

    private static int WriteResponse(LidGuardPipeResponse response, bool includeSessions = false, bool includeSettings = false)
    {
        if (!response.Succeeded)
        {
            Console.Error.WriteLine(response.Message);
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(response.Message)) Console.WriteLine(response.Message);
        Console.WriteLine($"Active sessions: {response.ActiveSessionCount}");
        Console.WriteLine($"Lid state: {response.LidSwitchState}");

        if (includeSessions)
        {
            foreach (var session in response.Sessions)
            {
                var processText = session.WatchedProcessIdentifier > 0 ? session.WatchedProcessIdentifier.ToString() : "none";
                Console.WriteLine(
                    $"- {session.Provider}:{session.SessionIdentifier} process={processText} softLock={DescribeSoftLockStatus(session)} cwd=\"{session.WorkingDirectory}\" started={session.StartedAt:O}");
            }
        }

        if (includeSettings) WriteSettings(response.Settings);

        return 0;
    }

    private static void WriteSettings(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        var powerRequest = normalizedSettings.PowerRequest ?? PowerRequestOptions.Default;
        Console.WriteLine("Settings:");
        Console.WriteLine($"  Prevent system sleep: {powerRequest.PreventSystemSleep}");
        Console.WriteLine($"  Prevent away mode sleep: {powerRequest.PreventAwayModeSleep}");
        Console.WriteLine($"  Prevent display sleep: {powerRequest.PreventDisplaySleep}");
        Console.WriteLine($"  Change lid action: {normalizedSettings.ChangeLidAction}");
        Console.WriteLine($"  Watch parent process: {normalizedSettings.WatchParentProcess}");
        Console.WriteLine($"  Suspend mode: {normalizedSettings.SuspendMode}");
        Console.WriteLine($"  Post-stop suspend delay seconds: {normalizedSettings.PostStopSuspendDelaySeconds}");
        Console.WriteLine($"  Post-stop suspend sound: {PostStopSuspendSoundConfiguration.GetDisplayValue(normalizedSettings.PostStopSuspendSound)}");
        Console.WriteLine($"  Pre-suspend webhook URL: {PreSuspendWebhookConfiguration.GetDisplayValue(normalizedSettings.PreSuspendWebhookUrl)}");
        Console.WriteLine($"  Closed lid permission request decision: {normalizedSettings.ClosedLidPermissionRequestDecision}");
        Console.WriteLine($"  Reason: {powerRequest.Reason}");
    }

    private static int WriteHelp(int exitCode)
    {
        var commandDisplayName = GetCommandDisplayName();

        Console.WriteLine("Usage:");
        Console.WriteLine($"  {commandDisplayName} start --provider codex|claude|copilot --session <id> [--parent-pid <pid>] [--working-directory <path>]");
        Console.WriteLine($"  {commandDisplayName} stop --provider codex|claude|copilot --session <id>");
        Console.WriteLine($"  {commandDisplayName} {LidGuardPipeCommands.RemovePreSuspendWebhook}");
        Console.WriteLine($"  {commandDisplayName} remove-session --session <id> [--provider codex|claude|copilot|custom|unknown]");
        Console.WriteLine($"  {commandDisplayName} claude-hook");
        Console.WriteLine($"  {commandDisplayName} claude-hooks [--format settings-json|hooks-json] [--executable <path>]");
        Console.WriteLine($"  {commandDisplayName} copilot-hook --event sessionStart|sessionEnd|userPromptSubmitted|preToolUse|postToolUse|permissionRequest|agentStop|errorOccurred|notification");
        Console.WriteLine($"  {commandDisplayName} copilot-hooks [--format config-json|hooks-json] [--executable <path>]");
        Console.WriteLine($"  {commandDisplayName} codex-hook");
        Console.WriteLine($"  {commandDisplayName} codex-hooks [--format config-toml|hooks-json] [--executable <path>]");
        Console.WriteLine($"  {commandDisplayName} hook-status [--provider codex|claude|copilot|all] [--config <path>] [--executable <path>]");
        Console.WriteLine($"  {commandDisplayName} hook-install [--provider codex|claude|copilot|all] [--config <path>] [--executable <path>]");
        Console.WriteLine($"  {commandDisplayName} hook-remove [--provider codex|claude|copilot|all] [--config <path>] [--executable <path>]");
        Console.WriteLine($"  {commandDisplayName} hook-events [--provider codex|claude|copilot|all] [--count <number>]");
        Console.WriteLine($"  {commandDisplayName} mcp-status [--provider codex|claude|copilot|all]");
        Console.WriteLine($"  {commandDisplayName} mcp-install [--provider codex|claude|copilot|all]");
        Console.WriteLine($"  {commandDisplayName} mcp-remove [--provider codex|claude|copilot|all]");
        Console.WriteLine("                           With --provider all, only providers with existing default configuration roots are processed.");
        Console.WriteLine("                           Missing providers are reported and skipped.");
        Console.WriteLine($"  {commandDisplayName} preview-system-sound --name Asterisk|Beep|Exclamation|Hand|Question");
        Console.WriteLine($"  {commandDisplayName} {LidGuardMcpServerCommand.CommandName}");
        Console.WriteLine($"  {commandDisplayName} settings");
        Console.WriteLine($"  {commandDisplayName} settings [--reset true] [--change-lid-action true|false]");
        Console.WriteLine("                           [--prevent-system-sleep true|false] [--prevent-away-mode-sleep true|false] [--prevent-display-sleep true|false]");
        Console.WriteLine("                           [--watch-parent-process true|false]");
        Console.WriteLine("                           [--suspend-mode sleep|hibernate] [--post-stop-suspend-delay-seconds <number>]");
        Console.WriteLine("                           [--post-stop-suspend-sound off|Asterisk|Beep|Exclamation|Hand|Question|<wav-path>]");
        Console.WriteLine("                           [--pre-suspend-webhook-url <http-or-https-url>]");
        Console.WriteLine("                           [--closed-lid-permission-request-decision deny|allow]");
        Console.WriteLine("                           [--power-request-reason <text>]");
        Console.WriteLine("                           Post-stop suspend delay defaults to 10 seconds; use 0 for immediate suspend.");
        Console.WriteLine($"                           Post-stop suspend sound defaults to off. Supported SystemSounds: {DescribeSupportedPostStopSuspendSystemSounds()}.");
        Console.WriteLine($"                           Use {LidGuardPipeCommands.RemovePreSuspendWebhook} to remove the pre-suspend webhook URL.");
        Console.WriteLine("                           Pre-suspend webhook URL must be an absolute HTTP or HTTPS URL when set.");
        Console.WriteLine("                           WAV paths must point to an existing playable .wav file.");
        Console.WriteLine($"  {commandDisplayName} status");
        Console.WriteLine($"  {commandDisplayName} cleanup-orphans");
        Console.WriteLine();
        Console.WriteLine($"Settings file: {LidGuardSettingsStore.GetDefaultSettingsFilePath()}");
        Console.WriteLine($"Session log: {LidGuardRuntimeSessionLogStore.GetDefaultLogFilePath()}");
        return exitCode;
    }

    private static string GetCommandDisplayName()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return "lidguard";

        var fileName = Path.GetFileNameWithoutExtension(processPath);
        return string.IsNullOrWhiteSpace(fileName) ? "lidguard" : fileName;
    }

    private static int WriteUnknownCommand(string commandName)
    {
        Console.Error.WriteLine($"Unknown command: {commandName}");
        return WriteHelp(1);
    }

    private static string DescribeSupportedPostStopSuspendSystemSounds() => string.Join(", ", s_supportedPostStopSuspendSystemSoundNames);

    private static string DescribeSoftLockStatus(LidGuardSessionStatus session)
    {
        if (session.SoftLockState != LidGuardSessionSoftLockState.SoftLocked) return session.SoftLockState.ToString();

        var details = session.SoftLockState.ToString();
        if (!string.IsNullOrWhiteSpace(session.SoftLockReason)) details = $"{details}:{session.SoftLockReason}";
        if (session.SoftLockedAt is not null) details = $"{details}@{session.SoftLockedAt.Value:O}";
        return details;
    }
}

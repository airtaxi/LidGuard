using System.Diagnostics;
using LidGuard.Hooks;
using LidGuard.Ipc;
using LidGuard.Mcp;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuard.Platform;
using LidGuard.Power;
using LidGuard.Services;

namespace LidGuard.Commands;

internal static class LidGuardCommandLineApplication
{
    public static async Task<int> RunAsync(string[] commandLineArguments)
    {
        var runtimePlatform = new LidGuardRuntimePlatform();
        if (!runtimePlatform.IsSupported) return WriteUnsupportedPlatform(runtimePlatform);

        if (commandLineArguments.Length == 0) return LidGuardCommandConsole.WriteHelp(1);

        var requestedCommandName = commandLineArguments[0].Trim();
        var commandName = requestedCommandName.ToLowerInvariant();
        if (commandName == LidGuardPipeCommands.Help) return WriteTopLevelHelp(commandLineArguments);
        if (IsTopLevelHelpAlias(commandName)) return LidGuardCommandConsole.WriteHelp(0);
        if (!TryRestorePendingLidActionBackup(runtimePlatform, out var recoveryMessage))
        {
            Console.Error.WriteLine(recoveryMessage);
            return 1;
        }

        if (HasCommandHelpSwitch(commandLineArguments)) return LidGuardCommandConsole.WriteHelpForCommand(requestedCommandName);

        if (commandName == LidGuardPipeCommands.ClaudeHook) return await ClaudeHookCommand.RunAsync();
        if (commandName == LidGuardPipeCommands.CopilotHook) return await GitHubCopilotHookCommand.RunAsync(commandLineArguments[1..]);
        if (commandName == LidGuardPipeCommands.CodexHook) return await CodexHookCommand.RunAsync();
        if (commandName == LidGuardMcpServerCommand.CommandName) return await LidGuardMcpServerCommand.RunAsync(commandLineArguments[1..]);
        if (commandName == ProviderMcpServerCommand.CommandName) return await ProviderMcpServerCommand.RunAsync(commandLineArguments[1..]);
        if (commandName == LidGuardPipeCommands.RunServer) return await RunServerAsync(runtimePlatform);
#if LIDGUARD_LINUX
        if (commandName == LinuxPermissionCommand.CommandName) return LinuxPermissionCommand.Run(commandLineArguments[1..]);
#endif

        if (!IsOptionParsedCommandName(commandName)) return LidGuardCommandConsole.WriteUnknownCommand(requestedCommandName);

        if (!CommandOptionReader.TryParseOptions(commandLineArguments, 1, out var options, out var parseMessage))
        {
            Console.Error.WriteLine(parseMessage);
            return 1;
        }

        return commandName switch
        {
            LidGuardPipeCommands.Start => await SendStartAsync(options),
            LidGuardPipeCommands.Stop => await SendStopAsync(options),
            LidGuardPipeCommands.RemovePreSuspendWebhook => await LidGuardSettingsCommand.SendRemovePreSuspendWebhookAsync(options, runtimePlatform),
            LidGuardPipeCommands.RemovePostSessionEndWebhook => await LidGuardSettingsCommand.SendRemovePostSessionEndWebhookAsync(options, runtimePlatform),
            LidGuardPipeCommands.RemoveSession => await SendRemoveSessionAsync(options),
            LidGuardPipeCommands.Status => await SendStatusAsync(),
            LidGuardPipeCommands.CleanupOrphans => await SendCleanupOrphansAsync(),
            LidGuardPipeCommands.CurrentLidState => WriteCurrentLidState(runtimePlatform),
            LidGuardPipeCommands.CurrentMonitorCount => WriteCurrentMonitorCount(runtimePlatform),
            LidGuardPipeCommands.CurrentTemperature => WriteCurrentTemperature(options),
            LidGuardPipeCommands.SuspendHistory => SuspendHistoryCommand.WriteHistory(options),
            LidGuardPipeCommands.Settings => await LidGuardSettingsCommand.SendSettingsAsync(options, runtimePlatform),
            LidGuardPipeCommands.PreviewCurrentSound => LidGuardSettingsCommand.PreviewCurrentSound(options, runtimePlatform),
            LidGuardPipeCommands.PreviewSystemSound => LidGuardSettingsCommand.PreviewSystemSound(options, runtimePlatform),
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
            LidGuardPipeCommands.ProviderMcpStatus => ProviderMcpManagementCommand.WriteProviderMcpStatus(options),
            LidGuardPipeCommands.ProviderMcpInstall => ProviderMcpManagementCommand.InstallProviderMcp(options),
            LidGuardPipeCommands.ProviderMcpRemove or "provider-mcp-uninstall" => ProviderMcpManagementCommand.RemoveProviderMcp(options),
            _ => LidGuardCommandConsole.WriteUnknownCommand(requestedCommandName)
        };
    }

    private static int WriteTopLevelHelp(string[] commandLineArguments)
    {
        if (commandLineArguments.Length == 1) return LidGuardCommandConsole.WriteHelp(0);
        if (commandLineArguments.Length == 2) return LidGuardCommandConsole.WriteHelpForCommand(commandLineArguments[1]);

        Console.Error.WriteLine($"Unexpected argument: {commandLineArguments[2]}");
        LidGuardCommandConsole.TryWriteHelpForCommand(LidGuardPipeCommands.Help, out _);
        return 1;
    }

    private static bool IsTopLevelHelpAlias(string commandName)
        => commandName is "--help" or "-h" or "/?";

    private static bool HasCommandHelpSwitch(string[] commandLineArguments)
    {
        for (var argumentIndex = 1; argumentIndex < commandLineArguments.Length; argumentIndex++)
        {
            if (commandLineArguments[argumentIndex].Equals("--help", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsOptionParsedCommandName(string commandName)
        => commandName is LidGuardPipeCommands.Start
            or LidGuardPipeCommands.Stop
            or LidGuardPipeCommands.RemovePreSuspendWebhook
            or LidGuardPipeCommands.RemovePostSessionEndWebhook
            or LidGuardPipeCommands.RemoveSession
            or LidGuardPipeCommands.Status
            or LidGuardPipeCommands.CleanupOrphans
            or LidGuardPipeCommands.CurrentLidState
            or LidGuardPipeCommands.CurrentMonitorCount
            or LidGuardPipeCommands.CurrentTemperature
            or LidGuardPipeCommands.SuspendHistory
            or LidGuardPipeCommands.Settings
            or LidGuardPipeCommands.PreviewCurrentSound
            or LidGuardPipeCommands.PreviewSystemSound
            or LidGuardPipeCommands.ClaudeHooks
            or LidGuardPipeCommands.CopilotHooks
            or LidGuardPipeCommands.CodexHooks
            or LidGuardPipeCommands.HookStatus
            or LidGuardPipeCommands.HookInstall
            or LidGuardPipeCommands.HookRemove
            or "hook-uninstall"
            or LidGuardPipeCommands.HookEvents
            or LidGuardPipeCommands.McpStatus
            or LidGuardPipeCommands.McpInstall
            or LidGuardPipeCommands.McpRemove
            or "mcp-uninstall"
            or LidGuardPipeCommands.ProviderMcpStatus
            or LidGuardPipeCommands.ProviderMcpInstall
            or LidGuardPipeCommands.ProviderMcpRemove
            or "provider-mcp-uninstall";

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
                serviceSet.SystemAudioVolumeController,
                serviceSet.LidStateSource,
                serviceSet.VisibleDisplayMonitorCountProvider,
                cancellationTokenSource.Cancel);

            var pipeServer = new LidGuardPipeServer(runtimeCoordinator, cancellationTokenSource.Cancel);

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
        if (!LidGuardSessionRequestFactory.TryCreateSessionRequest(options, LidGuardPipeCommands.Start, true, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var response = await new LidGuardRuntimeClient().SendAsync(request, true);
        return LidGuardCommandConsole.WriteResponse(response);
    }

    private static async Task<int> SendStopAsync(IReadOnlyDictionary<string, string> options)
    {
        if (!LidGuardSessionRequestFactory.TryCreateSessionRequest(options, LidGuardPipeCommands.Stop, false, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        return LidGuardCommandConsole.WriteResponse(response);
    }

    private static async Task<int> SendRemoveSessionAsync(IReadOnlyDictionary<string, string> options)
    {
        if (!LidGuardSessionRequestFactory.TryCreateSessionRemovalRequest(options, out var request, out var message))
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

        return LidGuardCommandConsole.WriteResponse(response);
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
                LidGuardCommandConsole.WriteSettings(settings);
            }
            else
            {
                Console.Error.WriteLine(settingsMessage);
            }

            return 0;
        }

        return LidGuardCommandConsole.WriteResponse(response, true, true);
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

        return LidGuardCommandConsole.WriteResponse(response);
    }

    private static int WriteCurrentTemperature(IReadOnlyDictionary<string, string> options)
    {
        if (!TryResolveCurrentTemperatureMode(options, out var emergencyHibernationTemperatureMode, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var currentTemperatureCelsius = GetCurrentTemperatureCelsius(emergencyHibernationTemperatureMode);
        if (!currentTemperatureCelsius.HasValue)
        {
            Console.WriteLine(
                $"Current recognized system temperature is unavailable from this platform's thermal-zone information using {emergencyHibernationTemperatureMode} mode.");
            return 0;
        }

        Console.WriteLine(
            $"Current recognized system temperature using {emergencyHibernationTemperatureMode} mode: {currentTemperatureCelsius.Value} Celsius");
        return 0;
    }

    private static int WriteCurrentMonitorCount(ILidGuardRuntimePlatform runtimePlatform)
    {
        var serviceSetResult = runtimePlatform.CreateRuntimeServiceSet();
        if (!serviceSetResult.Succeeded)
        {
            Console.Error.WriteLine(serviceSetResult.Message);
            return 1;
        }

        using var serviceSet = serviceSetResult.Value;
        var visibleDisplayMonitorCount = serviceSet.VisibleDisplayMonitorCountProvider.GetVisibleDisplayMonitorCount();
        Console.WriteLine($"Current visible display monitor count: {visibleDisplayMonitorCount}");
        return 0;
    }

    private static int WriteCurrentLidState(ILidGuardRuntimePlatform runtimePlatform)
    {
        var serviceSetResult = runtimePlatform.CreateRuntimeServiceSet();
        if (!serviceSetResult.Succeeded)
        {
            Console.Error.WriteLine(serviceSetResult.Message);
            return 1;
        }

        using var serviceSet = serviceSetResult.Value;
        var lidSwitchState = ReadCurrentLidSwitchState(serviceSet.LidStateSource);
        Console.WriteLine($"Current lid state: {lidSwitchState}");
        return 0;
    }

    private static LidSwitchState ReadCurrentLidSwitchState(ILidStateSource lidStateSource)
    {
        var lidSwitchState = lidStateSource.CurrentState;
        if (lidSwitchState != LidSwitchState.Unknown) return lidSwitchState;

        var stopwatch = Stopwatch.StartNew();
        while (lidSwitchState == LidSwitchState.Unknown && stopwatch.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            Thread.Sleep(25);
            lidSwitchState = lidStateSource.CurrentState;
        }

        return lidSwitchState;
    }

    private static int? GetCurrentTemperatureCelsius(EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
        => SystemThermalInformation.GetSystemTemperatureCelsius(emergencyHibernationTemperatureMode);

    private static bool TryResolveCurrentTemperatureMode(
        IReadOnlyDictionary<string, string> options,
        out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode,
        out string message)
    {
        emergencyHibernationTemperatureMode = LidGuardSettings.HeadlessRuntimeDefault.EmergencyHibernationTemperatureMode;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var temperatureModeText, "temperature-mode")) return TryLoadCurrentTemperatureModeFromSettings(out emergencyHibernationTemperatureMode, out message);

        if (string.IsNullOrWhiteSpace(temperatureModeText) || temperatureModeText.Trim().Equals("default", StringComparison.OrdinalIgnoreCase)) return TryLoadCurrentTemperatureModeFromSettings(out emergencyHibernationTemperatureMode, out message);
        if (LidGuardSettingsCommand.TryParseEmergencyHibernationTemperatureMode(temperatureModeText, out emergencyHibernationTemperatureMode)) return true;

        message = "The temperature-mode option must be default, low, average, or high.";
        return false;
    }

    private static bool TryLoadCurrentTemperatureModeFromSettings(
        out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode,
        out string message)
    {
        emergencyHibernationTemperatureMode = LidGuardSettings.HeadlessRuntimeDefault.EmergencyHibernationTemperatureMode;
        message = string.Empty;
        if (!LidGuardSettingsStore.TryLoadExistingOrDefault(out var settings, out _, out message)) return false;

        emergencyHibernationTemperatureMode = LidGuardSettings.Normalize(settings).EmergencyHibernationTemperatureMode;
        return true;
    }
}

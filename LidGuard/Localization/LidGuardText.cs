using System.Globalization;
using System.Resources;
using LidGuard.Power;
using LidGuard.Sessions;
using LidGuard.Settings;

namespace LidGuard.Localization;

internal static class LidGuardText
{
    private static readonly ResourceManager s_resourceManager = new("LidGuard.Resources.LidGuardText", typeof(LidGuardText).Assembly);

    public static string CommandDoesNotAcceptOptions(string commandName) => Format(nameof(CommandDoesNotAcceptOptions), commandName);

    public static string CommandUnexpectedArgument(string argument) => Format(nameof(CommandUnexpectedArgument), argument);

    public static string CommandUnknownCommand(string commandName) => Format(nameof(CommandUnknownCommand), commandName);

    public static string ConsoleActiveSessions(int activeSessionCount) => Format(nameof(ConsoleActiveSessions), activeSessionCount);

    public static string ConsoleCurrentLidState(object lidSwitchState) => Format(nameof(ConsoleCurrentLidState), lidSwitchState);

    public static string ConsoleCurrentMonitorCount(int visibleDisplayMonitorCount) => Format(nameof(ConsoleCurrentMonitorCount), visibleDisplayMonitorCount);

    public static string ConsoleLidState(object lidSwitchState) => Format(nameof(ConsoleLidState), lidSwitchState);

    public static string ConsoleRuntimeNotRunning => Get(nameof(ConsoleRuntimeNotRunning));

    public static string ConsoleRuntimeNotRunningNoCleanup => Get(nameof(ConsoleRuntimeNotRunningNoCleanup));

    public static string ConsoleRuntimeNotRunningNoSessionRemoved => Get(nameof(ConsoleRuntimeNotRunningNoSessionRemoved));

    public static string ConsoleSessionLine(
        string providerDisplayText,
        string sessionIdentifier,
        string processText,
        string softLockText,
        string workingDirectory,
        string startedAt,
        string lastActivityAt)
        => Format(nameof(ConsoleSessionLine), providerDisplayText, sessionIdentifier, processText, softLockText, workingDirectory, startedAt, lastActivityAt);

    public static string ConsoleSettingsFile(string settingsFilePath) => Format(nameof(ConsoleSettingsFile), settingsFilePath);

    public static string ConsoleVisibleDisplayMonitorCount(int visibleDisplayMonitorCount) => Format(nameof(ConsoleVisibleDisplayMonitorCount), visibleDisplayMonitorCount);

    public static string CultureInvalidUserInterfaceCulture(string cultureName, string detail) => Format(nameof(CultureInvalidUserInterfaceCulture), cultureName, detail);

    public static string CultureInvalidUserInterfaceCultureWarning(string cultureName, string detail) => Format(nameof(CultureInvalidUserInterfaceCultureWarning), cultureName, detail);

    public static string CultureSettingsLoadWarning(string detail) => Format(nameof(CultureSettingsLoadWarning), detail);

    public static string HelpAliasLabel => Get(nameof(HelpAliasLabel));

    public static string HelpCommandSpecificHelpHint => Get(nameof(HelpCommandSpecificHelpHint));

    public static string HelpNoteLabel => Get(nameof(HelpNoteLabel));

    public static string HelpOptionLabel(string optionLabel, string description) => Format(nameof(HelpOptionLabel), optionLabel, description);

    public static string HelpSettingsPostStopSuspendSoundOption(string supportedSystemSounds) => Format(nameof(HelpSettingsPostStopSuspendSoundOption), supportedSystemSounds);

    public static string HelpSessionLogFile(string sessionLogFilePath) => Format(nameof(HelpSessionLogFile), sessionLogFilePath);

    public static string HelpSectionDiagnostics => Get(nameof(HelpSectionDiagnostics));

    public static string HelpSectionHookIntegration => Get(nameof(HelpSectionHookIntegration));

    public static string HelpSectionManagedAndInternalCommands => Get(nameof(HelpSectionManagedAndInternalCommands));

    public static string HelpSectionMcpIntegration => Get(nameof(HelpSectionMcpIntegration));

    public static string HelpSectionPathsAndNotes => Get(nameof(HelpSectionPathsAndNotes));

    public static string HelpSectionSessionControl => Get(nameof(HelpSectionSessionControl));

    public static string HelpSectionSettingsAndSuspend => Get(nameof(HelpSectionSettingsAndSuspend));

    public static string HelpSectionUsage => Get(nameof(HelpSectionUsage));

    public static string HelpSuspendHistoryLogFile(string suspendHistoryLogFilePath) => Format(nameof(HelpSuspendHistoryLogFile), suspendHistoryLogFilePath);

    public static string HookManagementAlreadyInstalled(string providerName) => Format(nameof(HookManagementAlreadyInstalled), providerName);

    public static string HookManagementAlreadyInstalledOutsideManagedBlock(string providerName) => Format(nameof(HookManagementAlreadyInstalledOutsideManagedBlock), providerName);

    public static string HookManagementConfigurationFileDoesNotExist(string providerName) => Format(nameof(HookManagementConfigurationFileDoesNotExist), providerName);

    public static string HookManagementHookExecutableDoesNotExist(string executablePath) => Format(nameof(HookManagementHookExecutableDoesNotExist), executablePath);

    public static string HookManagementInstalled(string providerName) => Format(nameof(HookManagementInstalled), providerName);

    public static string HookManagementIsInstalled(string providerName) => Format(nameof(HookManagementIsInstalled), providerName);

    public static string HookManagementInstalledNeedsUpdate(string providerName) => Format(nameof(HookManagementInstalledNeedsUpdate), providerName);

    public static string HookManagementNoManagedHookFound(string providerName) => Format(nameof(HookManagementNoManagedHookFound), providerName);

    public static string HookManagementNotInstalled(string providerName) => Format(nameof(HookManagementNotInstalled), providerName);

    public static string HookManagementRemoved(string providerName) => Format(nameof(HookManagementRemoved), providerName);

    public static string HookManagementUnsupportedInstallation(string providerName) => Format(nameof(HookManagementUnsupportedInstallation), providerName);

    public static string HookManagementUnsupportedRemoval(string providerName) => Format(nameof(HookManagementUnsupportedRemoval), providerName);

    public static string HookManagementWrittenNeedsAttention(string providerName) => Format(nameof(HookManagementWrittenNeedsAttention), providerName);

    public static string HookStatusMessageBlockingClosedLidAskUserPrompt => Get(nameof(HookStatusMessageBlockingClosedLidAskUserPrompt));

    public static string HookStatusMessageCancelingClosedLidElicitationRequest => Get(nameof(HookStatusMessageCancelingClosedLidElicitationRequest));

    public static string HookStatusMessageRecordingClaudeFailedToolActivity => Get(nameof(HookStatusMessageRecordingClaudeFailedToolActivity));

    public static string HookStatusMessageRecordingClaudeBackgroundTaskActivity => Get(nameof(HookStatusMessageRecordingClaudeBackgroundTaskActivity));

    public static string HookStatusMessageRecordingClaudeBackgroundTaskCompletion => Get(nameof(HookStatusMessageRecordingClaudeBackgroundTaskCompletion));

    public static string HookStatusMessageRecordingClaudeSoftLockTelemetry => Get(nameof(HookStatusMessageRecordingClaudeSoftLockTelemetry));

    public static string HookStatusMessageRecordingClaudeSubagentActivity => Get(nameof(HookStatusMessageRecordingClaudeSubagentActivity));

    public static string HookStatusMessageRecordingClaudeSubagentCompletionActivity => Get(nameof(HookStatusMessageRecordingClaudeSubagentCompletionActivity));

    public static string HookStatusMessageRecordingClaudeToolActivity => Get(nameof(HookStatusMessageRecordingClaudeToolActivity));

    public static string HookStatusMessageRecordingClaudeToolCompletionActivity => Get(nameof(HookStatusMessageRecordingClaudeToolCompletionActivity));

    public static string HookStatusMessageRecordingGitHubCopilotErrorTelemetry => Get(nameof(HookStatusMessageRecordingGitHubCopilotErrorTelemetry));

    public static string HookStatusMessageRecordingGitHubCopilotPromptTelemetry => Get(nameof(HookStatusMessageRecordingGitHubCopilotPromptTelemetry));

    public static string HookStatusMessageRecordingGitHubCopilotSessionEnd => Get(nameof(HookStatusMessageRecordingGitHubCopilotSessionEnd));

    public static string HookStatusMessageRecordingGitHubCopilotSessionStart => Get(nameof(HookStatusMessageRecordingGitHubCopilotSessionStart));

    public static string HookStatusMessageRecordingGitHubCopilotToolCompletionActivity => Get(nameof(HookStatusMessageRecordingGitHubCopilotToolCompletionActivity));

    public static string HookStatusMessageRespondingToClosedLidPermissionRequest => Get(nameof(HookStatusMessageRespondingToClosedLidPermissionRequest));

    public static string HookStatusMessageStartingTurnProtection => Get(nameof(HookStatusMessageStartingTurnProtection));

    public static string HookStatusMessageStoppingSessionProtection => Get(nameof(HookStatusMessageStoppingSessionProtection));

    public static string HookStatusMessageStoppingTurnProtection => Get(nameof(HookStatusMessageStoppingTurnProtection));

    public static string ManagementBackup(string backupFilePath) => Format(nameof(ManagementBackup), backupFilePath);

    public static string ManagementChanged(bool changed) => Format(nameof(ManagementChanged), DisplayBoolean(changed));

    public static string ManagementCommand(string value) => Format(nameof(ManagementCommand), value);

    public static string ManagementCommandEmpty => Get(nameof(ManagementCommandEmpty));

    public static string ManagementConfigurationFileDoesNotExist(string configurationFilePath) => Format(nameof(ManagementConfigurationFileDoesNotExist), configurationFilePath);

    public static string ManagementConfig(string configurationFilePath) => Format(nameof(ManagementConfig), configurationFilePath);

    public static string ManagementConfigExists(bool exists) => Format(nameof(ManagementConfigExists), DisplayBoolean(exists));

    public static string ManagementContainsMcpServerCommand(bool containsMcpServerCommand) => Format(nameof(ManagementContainsMcpServerCommand), DisplayBoolean(containsMcpServerCommand));

    public static string ManagementFailedToStartProcess(string fileName) => Format(nameof(ManagementFailedToStartProcess), fileName);

    public static string ManagementField(string label, object value) => Format(nameof(ManagementField), label, value);

    public static string ManagementHookEventsTitle(object provider) => Format(nameof(ManagementHookEventsTitle), provider);

    public static string ManagementHookInstallationTitle => Get(nameof(ManagementHookInstallationTitle));

    public static string ManagementHookStatusTitle(object provider) => Format(nameof(ManagementHookStatusTitle), provider);

    public static string ManagementInstalled(bool installed) => Format(nameof(ManagementInstalled), DisplayBoolean(installed));

    public static string ManagementInstallingHook(object provider) => Format(nameof(ManagementInstallingHook), provider);

    public static string ManagementInstallingMcpServer(string providerName) => Format(nameof(ManagementInstallingMcpServer), providerName);

    public static string ManagementMcpInstallationTitle => Get(nameof(ManagementMcpInstallationTitle));

    public static string ManagementMcpStatusTitle(string providerName) => Format(nameof(ManagementMcpStatusTitle), providerName);

    public static string ManagementMessage(string message) => Format(nameof(ManagementMessage), message);

    public static string ManagementNoAvailableProviders => Get(nameof(ManagementNoAvailableProviders));

    public static string ManagementNoMcpServerNamedFound(string serverName) => Format(nameof(ManagementNoMcpServerNamedFound), serverName);

    public static string ManagementNoProviderMcpServerNamedFound(string serverName, string configurationFilePath) => Format(nameof(ManagementNoProviderMcpServerNamedFound), serverName, configurationFilePath);

    public static string ManagementNoProviderMcpServerNamedRemoved(string serverName) => Format(nameof(ManagementNoProviderMcpServerNamedRemoved), serverName);

    public static string ManagementNoProviderMcpServerEntryFound => Get(nameof(ManagementNoProviderMcpServerEntryFound));

    public static string ManagementProvider(object provider) => Format(nameof(ManagementProvider), provider);

    public static string ManagementProviderCli(string value) => Format(nameof(ManagementProviderCli), value);

    public static string ManagementProviderCliAvailable(bool available) => Format(nameof(ManagementProviderCliAvailable), DisplayBoolean(available));

    public static string ManagementProviderCliNotFound(string providerName, string checkedPaths) => Format(nameof(ManagementProviderCliNotFound), providerName, checkedPaths);

    public static string ManagementProviderMcpInstallationTitle => Get(nameof(ManagementProviderMcpInstallationTitle));

    public static string ManagementProviderMcpInstalled(string serverName, string configurationFilePath) => Format(nameof(ManagementProviderMcpInstalled), serverName, configurationFilePath);

    public static string ManagementProviderMcpRegistered => Get(nameof(ManagementProviderMcpRegistered));

    public static string ManagementProviderMcpRemoved(string serverName, string configurationFilePath) => Format(nameof(ManagementProviderMcpRemoved), serverName, configurationFilePath);

    public static string ManagementProviderName(string providerName) => Format(nameof(ManagementProviderName), providerName);

    public static string ManagementProviderSelectionPrompt(string prompt) => Format(nameof(ManagementProviderSelectionPrompt), prompt);

    public static string ManagementRemovingHook(object provider) => Format(nameof(ManagementRemovingHook), provider);

    public static string ManagementRemovingMcpServer(string providerName) => Format(nameof(ManagementRemovingMcpServer), providerName);

    public static string ManagementRequiredStopHooks(bool value) => Format(nameof(ManagementRequiredStopHooks), DisplayBoolean(value));

    public static string ManagementServerArguments(string value) => Format(nameof(ManagementServerArguments), value);

    public static string ManagementServerName(string serverName) => Format(nameof(ManagementServerName), serverName);

    public static string ManagementSkippedAbsentProvider(string providerName, string candidatePaths) => Format(nameof(ManagementSkippedAbsentProvider), providerName, candidatePaths);

    public static string ManagementStatus(object status) => Format(nameof(ManagementStatus), status);

    public static string ManagementTransport(string value) => Format(nameof(ManagementTransport), value);

    public static string ManagementUnsupportedHookEventLogs => Get(nameof(ManagementUnsupportedHookEventLogs));

    public static string ManagementUnsupportedHookManagement => Get(nameof(ManagementUnsupportedHookManagement));

    public static string ManagementUnsupportedMcpManagement => Get(nameof(ManagementUnsupportedMcpManagement));

    public static string ManagementUnsupportedProviderSelection => Get(nameof(ManagementUnsupportedProviderSelection));

    public static string ManagementUrl(string value) => Format(nameof(ManagementUrl), value);

    public static string SettingsChangeLidAction(object value) => Format(nameof(SettingsChangeLidAction), value);

    public static string SettingsClosedLidPermissionRequestDecision(object value) => Format(nameof(SettingsClosedLidPermissionRequestDecision), value);

    public static string SettingsEmergencyHibernationOnHighTemperature(object value) => Format(nameof(SettingsEmergencyHibernationOnHighTemperature), value);

    public static string SettingsEmergencyHibernationTemperatureCelsius(int value) => Format(nameof(SettingsEmergencyHibernationTemperatureCelsius), value);

    public static string SettingsEmergencyHibernationTemperatureMode(object value) => Format(nameof(SettingsEmergencyHibernationTemperatureMode), value);

    public static string SettingsNoPostSessionEndWebhookConfigured => Get(nameof(SettingsNoPostSessionEndWebhookConfigured));

    public static string SettingsNoPreSuspendWebhookConfigured => Get(nameof(SettingsNoPreSuspendWebhookConfigured));

    public static string SettingsNameChangeLidAction => Get(nameof(SettingsNameChangeLidAction));

    public static string SettingsNameClosedLidPermissionRequestDecision => Get(nameof(SettingsNameClosedLidPermissionRequestDecision));

    public static string SettingsNameEmergencyHibernationOnHighTemperature => Get(nameof(SettingsNameEmergencyHibernationOnHighTemperature));

    public static string SettingsNameEmergencyHibernationTemperatureCelsius => Get(nameof(SettingsNameEmergencyHibernationTemperatureCelsius));

    public static string SettingsNameEmergencyHibernationTemperatureMode => Get(nameof(SettingsNameEmergencyHibernationTemperatureMode));

    public static string SettingsNamePostStopSuspendDelaySeconds => Get(nameof(SettingsNamePostStopSuspendDelaySeconds));

    public static string SettingsNamePostStopSuspendSound => Get(nameof(SettingsNamePostStopSuspendSound));

    public static string SettingsNamePostStopSuspendSoundVolumeOverridePercent => Get(nameof(SettingsNamePostStopSuspendSoundVolumeOverridePercent));

    public static string SettingsNamePreventAwayModeSleep => Get(nameof(SettingsNamePreventAwayModeSleep));

    public static string SettingsNamePreventDisplaySleep => Get(nameof(SettingsNamePreventDisplaySleep));

    public static string SettingsNamePreventSystemSleep => Get(nameof(SettingsNamePreventSystemSleep));

    public static string SettingsNameServerRuntimeCleanupDelayMinutes => Get(nameof(SettingsNameServerRuntimeCleanupDelayMinutes));

    public static string SettingsNameSessionTimeoutMinutes => Get(nameof(SettingsNameSessionTimeoutMinutes));

    public static string SettingsNameSuspendHistoryEntryCount => Get(nameof(SettingsNameSuspendHistoryEntryCount));

    public static string SettingsNameSuspendMode => Get(nameof(SettingsNameSuspendMode));

    public static string SettingsNameUserInterfaceCulture => Get(nameof(SettingsNameUserInterfaceCulture));

    public static string SettingsNameWatchParentProcess => Get(nameof(SettingsNameWatchParentProcess));

    public static string SettingsPostSessionEndWebhookUrl(string value) => Format(nameof(SettingsPostSessionEndWebhookUrl), value);

    public static string SettingsPostSessionEndWebhookUrlRemoved => Get(nameof(SettingsPostSessionEndWebhookUrlRemoved));

    public static string SettingsPostStopSuspendDelaySeconds(int value) => Format(nameof(SettingsPostStopSuspendDelaySeconds), value);

    public static string SettingsPostStopSuspendSound(string value) => Format(nameof(SettingsPostStopSuspendSound), value);

    public static string SettingsPostStopSuspendSoundVolumeOverridePercent(string value) => Format(nameof(SettingsPostStopSuspendSoundVolumeOverridePercent), value);

    public static string SettingsPreSuspendWebhookUrl(string value) => Format(nameof(SettingsPreSuspendWebhookUrl), value);

    public static string SettingsPreSuspendWebhookUrlRemoved => Get(nameof(SettingsPreSuspendWebhookUrlRemoved));

    public static string SettingsPreventAwayModeSleep(object value) => Format(nameof(SettingsPreventAwayModeSleep), value);

    public static string SettingsPreventDisplaySleep(object value) => Format(nameof(SettingsPreventDisplaySleep), value);

    public static string SettingsPreventSystemSleep(object value) => Format(nameof(SettingsPreventSystemSleep), value);

    public static string SettingsReason(string value) => Format(nameof(SettingsReason), value);

    public static string SettingsRuntimeNotRunningSaved => Get(nameof(SettingsRuntimeNotRunningSaved));

    public static string SettingsRuntimeUpdated => Get(nameof(SettingsRuntimeUpdated));

    public static string SettingsServerRuntimeCleanupDelay(string value) => Format(nameof(SettingsServerRuntimeCleanupDelay), value);

    public static string SettingsInteractiveBooleanValidation(string settingName) => Format(nameof(SettingsInteractiveBooleanValidation), settingName);

    public static string SettingsInteractiveClosedLidPermissionRequestDecisionValidation => Get(nameof(SettingsInteractiveClosedLidPermissionRequestDecisionValidation));

    public static string SettingsInteractiveEmergencyHibernationTemperatureModeValidation(string settingName) => Format(nameof(SettingsInteractiveEmergencyHibernationTemperatureModeValidation), settingName);

    public static string SettingsInteractiveInputEnded(string settingName) => Format(nameof(SettingsInteractiveInputEnded), settingName);

    public static string SettingsInteractiveNonNegativeIntegerValidation(string settingName) => Format(nameof(SettingsInteractiveNonNegativeIntegerValidation), settingName);

    public static string SettingsInteractivePostStopSuspendSoundDetails(string supportedSystemSounds) => Format(nameof(SettingsInteractivePostStopSuspendSoundDetails), supportedSystemSounds);

    public static string SettingsInteractivePrompt(string settingName, string storedValue, string defaultValue) => Format(nameof(SettingsInteractivePrompt), settingName, storedValue, defaultValue);

    public static string SettingsInteractivePromptWithDetails(string settingName, string storedValue, string defaultValue, string details) => Format(nameof(SettingsInteractivePromptWithDetails), settingName, storedValue, defaultValue, details);

    public static string SettingsInteractiveRangeDetails(int minimumValue, int maximumValue) => Format(nameof(SettingsInteractiveRangeDetails), minimumValue, maximumValue);

    public static string SettingsInteractiveRangeValidation(string settingName, int minimumValue, int maximumValue) => Format(nameof(SettingsInteractiveRangeValidation), settingName, minimumValue, maximumValue);

    public static string SettingsInteractiveServerRuntimeCleanupDelayDetails(int minimumValue) => Format(nameof(SettingsInteractiveServerRuntimeCleanupDelayDetails), minimumValue);

    public static string SettingsInteractiveSessionTimeoutDetails(int minimumValue) => Format(nameof(SettingsInteractiveSessionTimeoutDetails), minimumValue);

    public static string SettingsInteractiveSuspendHistoryEntryCountDetails(int minimumValue) => Format(nameof(SettingsInteractiveSuspendHistoryEntryCountDetails), minimumValue);

    public static string SettingsInteractiveSuspendModeValidation(string settingName) => Format(nameof(SettingsInteractiveSuspendModeValidation), settingName);

    public static string SettingsInteractiveVolumeOverrideDetails(int minimumValue, int maximumValue) => Format(nameof(SettingsInteractiveVolumeOverrideDetails), minimumValue, maximumValue);

    public static string SettingsInteractiveValueOffOrMinimumValidation(string settingName, int minimumValue) => Format(nameof(SettingsInteractiveValueOffOrMinimumValidation), settingName, minimumValue);

    public static string SettingsInteractiveVolumeOverrideValidation(string settingName, int minimumValue, int maximumValue) => Format(nameof(SettingsInteractiveVolumeOverrideValidation), settingName, minimumValue, maximumValue);

    public static string SettingsOptionClosedLidPermissionRequestDecisionValidation => Get(nameof(SettingsOptionClosedLidPermissionRequestDecisionValidation));

    public static string SettingsOptionEmergencyHibernationTemperatureCelsiusValidation(int minimumValue, int maximumValue) => Format(nameof(SettingsOptionEmergencyHibernationTemperatureCelsiusValidation), minimumValue, maximumValue);

    public static string SettingsOptionEmergencyHibernationTemperatureModeValidation => Get(nameof(SettingsOptionEmergencyHibernationTemperatureModeValidation));

    public static string SettingsOptionPostSessionEndWebhookRemovalCommand(string commandDisplayName, string commandName) => Format(nameof(SettingsOptionPostSessionEndWebhookRemovalCommand), commandDisplayName, commandName);

    public static string SettingsOptionPostStopSuspendDelaySecondsValidation => Get(nameof(SettingsOptionPostStopSuspendDelaySecondsValidation));

    public static string SettingsOptionPostStopSuspendSoundVolumeOverrideValidation(int minimumValue, int maximumValue) => Format(nameof(SettingsOptionPostStopSuspendSoundVolumeOverrideValidation), minimumValue, maximumValue);

    public static string SettingsOptionPreSuspendWebhookRemovalCommand(string commandDisplayName, string commandName) => Format(nameof(SettingsOptionPreSuspendWebhookRemovalCommand), commandDisplayName, commandName);

    public static string SettingsOptionServerRuntimeCleanupDelayValidation(int minimumValue) => Format(nameof(SettingsOptionServerRuntimeCleanupDelayValidation), minimumValue);

    public static string SettingsOptionSessionTimeoutValidation(int minimumValue) => Format(nameof(SettingsOptionSessionTimeoutValidation), minimumValue);

    public static string SettingsOptionSuspendHistoryCountValidation(int minimumValue) => Format(nameof(SettingsOptionSuspendHistoryCountValidation), minimumValue);

    public static string SettingsOptionSuspendModeValidation => Get(nameof(SettingsOptionSuspendModeValidation));

    public static string SettingsInteractiveGuidanceChangePostSessionEndWebhook(string commandDisplayName) => Format(nameof(SettingsInteractiveGuidanceChangePostSessionEndWebhook), commandDisplayName);

    public static string SettingsInteractiveGuidanceChangePreSuspendWebhook(string commandDisplayName) => Format(nameof(SettingsInteractiveGuidanceChangePreSuspendWebhook), commandDisplayName);

    public static string SettingsInteractiveGuidanceChangeReason(string commandDisplayName) => Format(nameof(SettingsInteractiveGuidanceChangeReason), commandDisplayName);

    public static string SettingsInteractiveGuidanceRemovePostSessionEndWebhook(string commandDisplayName, string commandName) => Format(nameof(SettingsInteractiveGuidanceRemovePostSessionEndWebhook), commandDisplayName, commandName);

    public static string SettingsInteractiveGuidanceRemovePreSuspendWebhook(string commandDisplayName, string commandName) => Format(nameof(SettingsInteractiveGuidanceRemovePreSuspendWebhook), commandDisplayName, commandName);

    public static string SettingsManagedHookStatusMessageRefreshChanged(string providerNames) => Format(nameof(SettingsManagedHookStatusMessageRefreshChanged), providerNames);

    public static string SettingsManagedHookStatusMessageRefreshFailed(string detail) => Format(nameof(SettingsManagedHookStatusMessageRefreshFailed), detail);

    public static string SettingsManagedHookStatusMessageRefreshUnchanged => Get(nameof(SettingsManagedHookStatusMessageRefreshUnchanged));

    public static string SettingsPreviewConfigureCurrentSound(string commandDisplayName) => Format(nameof(SettingsPreviewConfigureCurrentSound), commandDisplayName);

    public static string SettingsPreviewCurrentSoundNotConfigured => Get(nameof(SettingsPreviewCurrentSoundNotConfigured));

    public static string SettingsPreviewPlayableWavSupported => Get(nameof(SettingsPreviewPlayableWavSupported));

    public static string SettingsPreviewPlayedCurrentPostStopSuspendSound(string postStopSuspendSound) => Format(nameof(SettingsPreviewPlayedCurrentPostStopSuspendSound), postStopSuspendSound);

    public static string SettingsPreviewPlayedSystemSound(string systemSoundName) => Format(nameof(SettingsPreviewPlayedSystemSound), systemSoundName);

    public static string SettingsPreviewSupportedSystemSounds(string supportedSystemSounds) => Format(nameof(SettingsPreviewSupportedSystemSounds), supportedSystemSounds);

    public static string SettingsPreviewSystemSoundNameRequired(string supportedSystemSounds) => Format(nameof(SettingsPreviewSystemSoundNameRequired), supportedSystemSounds);

    public static string SettingsPreviewUnsupportedSystemSoundName(string systemSoundName) => Format(nameof(SettingsPreviewUnsupportedSystemSoundName), systemSoundName);

    public static string SettingsPreviewVolumeOverrideGuidance(string commandDisplayName) => Format(nameof(SettingsPreviewVolumeOverrideGuidance), commandDisplayName);

    public static string SettingsPreviewVolumeOverrideSetting(string value) => Format(nameof(SettingsPreviewVolumeOverrideSetting), value);

    public static string SettingsSessionTimeout(string value) => Format(nameof(SettingsSessionTimeout), value);

    public static string SettingsSuspendHistoryCount(string value) => Format(nameof(SettingsSuspendHistoryCount), value);

    public static string SettingsSuspendMode(object value) => Format(nameof(SettingsSuspendMode), value);

    public static string SettingsTitle => Get(nameof(SettingsTitle));

    public static string SettingsUserInterfaceCulture(string value) => Format(nameof(SettingsUserInterfaceCulture), value);

    public static string SettingsWatchParentProcess(object value) => Format(nameof(SettingsWatchParentProcess), value);

    public static string TextDisplayBooleanFalse => Get(nameof(TextDisplayBooleanFalse));

    public static string TextDisplayBooleanTrue => Get(nameof(TextDisplayBooleanTrue));

    public static string TextDisplayEmpty => Get(nameof(TextDisplayEmpty));

    public static string TextDisplayNone => Get(nameof(TextDisplayNone));

    public static string TextDisplayOff => Get(nameof(TextDisplayOff));

    public static string TextWarning(string message) => Format(nameof(TextWarning), message);

    public static string SessionProcessNone => Get(nameof(SessionProcessNone));

    public static string GetResourceString(string name, string fallbackValue) => s_resourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? fallbackValue;

    public static string DisplayBoolean(bool value) => value ? TextDisplayBooleanTrue : TextDisplayBooleanFalse;

    public static string DisplayClosedLidPermissionRequestDecision(ClosedLidPermissionRequestDecision value)
        => Get($"DisplayClosedLidPermissionRequestDecision{value}");

    public static string DisplayEmergencyHibernationTemperatureMode(EmergencyHibernationTemperatureMode value)
        => Get($"DisplayEmergencyHibernationTemperatureMode{value}");

    public static string DisplayLidSwitchState(LidSwitchState value)
        => Get($"DisplayLidSwitchState{value}");

    public static string DisplayMinuteCount(int? value)
        => value is null ? TextDisplayOff : Format(nameof(DisplayMinuteCount), value.Value);

    public static string DisplayOptionalValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TextDisplayNone;
        if (value.Equals("<none>", StringComparison.OrdinalIgnoreCase)) return TextDisplayNone;
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase)) return TextDisplayOff;
        return value;
    }

    public static string DisplaySessionSoftLockState(LidGuardSessionSoftLockState value)
        => Get($"DisplaySessionSoftLockState{value}");

    public static string DisplaySuspendMode(SystemSuspendMode value)
        => Get($"DisplaySuspendMode{value}");

    public static string DisplaySuspendHistoryEntryCount(int? value)
        => value is null ? TextDisplayOff : Format(nameof(DisplaySuspendHistoryEntryCount), value.Value);

    private static string Get(string name) => s_resourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    private static string Format(string name, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, Get(name), arguments);
}

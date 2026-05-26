using LidGuard.Power;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Sessions;
using LidGuard.Settings;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Commands;

namespace LidGuard.Control;

public sealed class LidGuardControlService(IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer)
{
    private readonly LidGuardRuntimeClient _runtimeClient = new();

    public async Task<LidGuardOperationResult<LidGuardControlSnapshot>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!LidGuardSettingsStore.TryLoadOrCreate(out var storedSettings, out var message)) return LidGuardOperationResult<LidGuardControlSnapshot>.Failure(message);

        var response = await _runtimeClient.SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false, cancellationToken);

        return LidGuardOperationResult<LidGuardControlSnapshot>.Success(CreateSnapshot(storedSettings, response));
    }

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> ClearSessionSoftLockAsync(string sessionIdentifier, AgentProvider provider, string providerName = "", string sessionStateReason = "", CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(LidGuardPipeCommands.MarkSessionActive, provider, providerName, sessionIdentifier, string.Empty, 0, sessionStateReason, false, string.Empty, false, false, false, cancellationToken);

    public async Task<LidGuardOperationResult<LidGuardSessionRemovalOutcome>> RemoveSessionAsync(string sessionIdentifier, AgentProvider? provider = null, string providerName = "", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure("A session identifier is required.");
        if (provider == AgentProvider.Mcp && string.IsNullOrWhiteSpace(providerName)) providerName = string.Empty;

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var storedSettings, out var message)) return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(message);

        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        var normalizedProviderName = provider is null ? string.Empty : AgentProviderDisplay.NormalizeProviderName(provider.Value, providerName);
        var statusResponse = await _runtimeClient.SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false, cancellationToken);
        if (!statusResponse.Succeeded && !statusResponse.RuntimeUnavailable) return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(statusResponse.Message);

        var removedSessions = GetMatchingSessions(statusResponse, sessionIdentifier, provider, normalizedProviderName);
        var removeRequest = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.RemoveSession,
            Provider = provider ?? AgentProvider.Unknown,
            ProviderName = normalizedProviderName,
            SessionIdentifier = sessionIdentifier,
            MatchAllProvidersForSessionIdentifier = provider is null,
            MatchAllProviderNamesForSessionIdentifier = provider == AgentProvider.Mcp && string.IsNullOrWhiteSpace(normalizedProviderName)
        };
        var removeResponse = await _runtimeClient.SendAsync(removeRequest, false, cancellationToken);
        if (!removeResponse.Succeeded && !removeResponse.RuntimeUnavailable) return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(removeResponse.Message);

        var removalOutcome = new LidGuardSessionRemovalOutcome
        {
            RequestedSessionIdentifier = sessionIdentifier,
            HasProviderFilter = provider is not null,
            RequestedProvider = provider ?? AgentProvider.Unknown,
            HasProviderNameFilter = !string.IsNullOrWhiteSpace(normalizedProviderName),
            RequestedProviderName = normalizedProviderName,
            RemovedSessions = removedSessions,
            Snapshot = CreateSnapshot(normalizedStoredSettings, removeResponse)
        };
        return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Success(removalOutcome);
    }

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> SetSessionSoftLockAsync(string sessionIdentifier, AgentProvider provider, string providerName = "", string sessionStateReason = "", CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(LidGuardPipeCommands.MarkSessionSoftLocked, provider, providerName, sessionIdentifier, string.Empty, 0, sessionStateReason, false, string.Empty, false, false, false, cancellationToken);

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> StartSessionAsync(string sessionIdentifier, AgentProvider provider, string providerName = "", string workingDirectory = "", int watchedProcessIdentifier = 0, CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(LidGuardPipeCommands.Start, provider, providerName, sessionIdentifier, workingDirectory, watchedProcessIdentifier, string.Empty, false, string.Empty, true, true, false, cancellationToken);

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> StopSessionAsync(string sessionIdentifier, AgentProvider provider, string providerName = "", bool isProviderSessionEnd = false, string sessionEndReason = "", CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(LidGuardPipeCommands.Stop, provider, providerName, sessionIdentifier, string.Empty, 0, string.Empty, isProviderSessionEnd, sessionEndReason, false, false, true, cancellationToken);

    public async Task<LidGuardOperationResult<LidGuardSettingsUpdateOutcome>> UpdateSettingsAsync(LidGuardSettingsPatch settingsPatch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsPatch);
        if (settingsPatch.PostStopSuspendDelaySeconds < 0) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure("Post-stop suspend delay seconds must be a non-negative integer.");
        if (settingsPatch.ClosedLidStopFollowUpDelaySeconds < 0) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure("Closed-lid stop follow-up delay seconds must be a non-negative integer.");
        if (settingsPatch.HasPostStopSuspendSoundVolumeOverridePercent && !PostStopSuspendSoundConfiguration.TryValidateVolumeOverridePercent(settingsPatch.PostStopSuspendSoundVolumeOverridePercent, out var volumeOverrideValidationMessage))
        {
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(volumeOverrideValidationMessage);
        }

        if (settingsPatch.HasClosedLidStopFollowUpSoundVolumeOverridePercent && !PostStopSuspendSoundConfiguration.TryValidateClosedLidStopFollowUpVolumeOverridePercent(settingsPatch.ClosedLidStopFollowUpSoundVolumeOverridePercent, out var closedLidStopFollowUpVolumeOverrideValidationMessage)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(closedLidStopFollowUpVolumeOverrideValidationMessage);

        if (settingsPatch.HasSuspendHistoryEntryCount && !SuspendHistoryConfiguration.TryValidateEntryCount(settingsPatch.SuspendHistoryEntryCount, out var suspendHistoryValidationMessage)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(suspendHistoryValidationMessage);

        if (settingsPatch.HasSessionTimeoutMinutes && !SessionTimeoutConfiguration.TryValidateMinutes(settingsPatch.SessionTimeoutMinutes, out var sessionTimeoutValidationMessage)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(sessionTimeoutValidationMessage);

        if (settingsPatch.HasServerRuntimeCleanupDelayMinutes && !ServerRuntimeCleanupConfiguration.TryValidateDelayMinutes(settingsPatch.ServerRuntimeCleanupDelayMinutes, out var serverRuntimeCleanupValidationMessage))
        {
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(serverRuntimeCleanupValidationMessage);
        }

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

        var previousStoredSettings = LidGuardSettings.Normalize(currentSettings);
        var updatedStoredSettings = ApplyPatch(previousStoredSettings, settingsPatch);
        if (!PostStopSuspendSoundConfiguration.TryNormalize(updatedStoredSettings, postStopSuspendSoundPlayer, out updatedStoredSettings, out message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

        if (settingsPatch.PreSuspendWebhookUrl is not null)
        {
            if (!PreSuspendWebhookConfiguration.TryNormalizeConfiguredValue(settingsPatch.PreSuspendWebhookUrl, out var normalizedPreSuspendWebhookUrl, out message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

            updatedStoredSettings = PreSuspendWebhookConfiguration.WithPreSuspendWebhookUrl(updatedStoredSettings, normalizedPreSuspendWebhookUrl);
        }

        if (settingsPatch.PostSessionEndWebhookUrl is not null)
        {
            if (!PostSessionEndWebhookConfiguration.TryNormalizeConfiguredValue(settingsPatch.PostSessionEndWebhookUrl, out var normalizedPostSessionEndWebhookUrl, out message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

            updatedStoredSettings = PostSessionEndWebhookConfiguration.WithPostSessionEndWebhookUrl(updatedStoredSettings, normalizedPostSessionEndWebhookUrl);
        }

        if (settingsPatch.ClosedLidStopFollowUpWebhookUrl is not null)
        {
            if (!ClosedLidStopFollowUpWebhookConfiguration.TryNormalizeConfiguredValue(settingsPatch.ClosedLidStopFollowUpWebhookUrl, out var normalizedClosedLidStopFollowUpWebhookUrl, out message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

            updatedStoredSettings = ClosedLidStopFollowUpWebhookConfiguration.WithClosedLidStopFollowUpWebhookUrl(updatedStoredSettings, normalizedClosedLidStopFollowUpWebhookUrl);
        }

        updatedStoredSettings = LidGuardSettings.Normalize(updatedStoredSettings);

        if (!LidGuardSettingsStore.TrySave(updatedStoredSettings, out message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);
        var managedHookRefreshResult = LidGuardSettingsChangeDetector.RequiresManagedHookRefresh(previousStoredSettings, updatedStoredSettings) ? ManagedHookStatusMessageRefresh.RefreshInstalledManagedHooks() : null;

        var request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.Settings,
            HasSettings = true,
            Settings = updatedStoredSettings
        };
        var response = await _runtimeClient.SendAsync(request, false, cancellationToken);

        var appliedChanges = LidGuardSettingsChangeDetector.DescribeChanges(previousStoredSettings, updatedStoredSettings);
        var updateOutcome = new LidGuardSettingsUpdateOutcome
        {
            ResetToDefaults = settingsPatch.ResetToDefaults,
            HadEffectiveChanges = appliedChanges.Length > 0,
            AppliedChanges = appliedChanges,
            PreviousStoredSettings = previousStoredSettings,
            UpdatedStoredSettings = updatedStoredSettings,
            ManagedHookRefreshResult = managedHookRefreshResult,
            Snapshot = CreateSnapshot(updatedStoredSettings, response)
        };
        return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Success(updateOutcome);
    }

    private static LidGuardControlSnapshot CreateSnapshot(LidGuardSettings storedSettings, LidGuardPipeResponse response)
    {
        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        if (!response.Succeeded)
        {
            LidGuardCulture.ApplyEffectiveCulture(normalizedStoredSettings);
            return new LidGuardControlSnapshot
            {
                SettingsFilePath = LidGuardSettingsStore.GetDefaultSettingsFilePath(),
                StoredSettings = normalizedStoredSettings,
                RuntimeReachable = false,
                RuntimeUnavailable = response.RuntimeUnavailable,
                RuntimeMessage = response.Message,
                RuntimeMessageCode = response.MessageCode,
                RuntimeMessageArguments = response.MessageArguments,
                ActiveSessionCount = response.ActiveSessionCount,
                ClosedLidStopFollowUpFeatureState = ClosedLidStopFollowUpConfiguration.GetFeatureState(normalizedStoredSettings),
                ClosedLidStopFollowUpConfigurationIssues = CreateClosedLidStopFollowUpConfigurationIssueMessages(normalizedStoredSettings),
                LidSwitchState = LidSwitchState.Unknown,
                VisibleDisplayMonitorCount = response.VisibleDisplayMonitorCount,
                Sessions = []
            };
        }

        LidGuardCulture.ApplyEffectiveCulture(response.Settings);
        var normalizedRuntimeSettings = LidGuardSettings.Normalize(response.Settings);
        return new LidGuardControlSnapshot
        {
            SettingsFilePath = LidGuardSettingsStore.GetDefaultSettingsFilePath(),
            StoredSettings = normalizedStoredSettings,
            RuntimeReachable = true,
            RuntimeUnavailable = false,
            RuntimeMessage = response.Message,
            RuntimeMessageCode = response.MessageCode,
            RuntimeMessageArguments = response.MessageArguments,
            HasRuntimeSettings = true,
            RuntimeSettings = normalizedRuntimeSettings,
            ClosedLidStopFollowUpFeatureState = ClosedLidStopFollowUpConfiguration.GetFeatureState(normalizedRuntimeSettings),
            ClosedLidStopFollowUpConfigurationIssues = CreateClosedLidStopFollowUpConfigurationIssueMessages(normalizedRuntimeSettings),
            ActiveSessionCount = response.ActiveSessionCount,
            LidSwitchState = response.LidSwitchState,
            VisibleDisplayMonitorCount = response.VisibleDisplayMonitorCount,
            Sessions = response.Sessions
        };
    }

    private static LidGuardSettings ApplyPatch(LidGuardSettings currentSettings, LidGuardSettingsPatch settingsPatch)
    {
        var baseSettings = settingsPatch.ResetToDefaults ? LidGuardSettings.HeadlessRuntimeDefault : currentSettings;
        var normalizedBaseSettings = LidGuardSettings.Normalize(baseSettings);
        var basePowerRequest = normalizedBaseSettings.PowerRequest ?? PowerRequestOptions.Default;

        return normalizedBaseSettings with
        {
            PowerRequest = basePowerRequest with
            {
                PreventSystemSleep = settingsPatch.PreventSystemSleep ?? basePowerRequest.PreventSystemSleep,
#if LIDGUARD_LINUX || LIDGUARD_MACOS
                PreventAwayModeSleep = false,
#else
                PreventAwayModeSleep = settingsPatch.PreventAwayModeSleep ?? basePowerRequest.PreventAwayModeSleep,
#endif
                PreventDisplaySleep = settingsPatch.PreventDisplaySleep ?? basePowerRequest.PreventDisplaySleep,
                Reason = settingsPatch.PowerRequestReason is null ? basePowerRequest.Reason : NormalizePowerRequestReason(settingsPatch.PowerRequestReason)
            },
            ChangeLidAction = settingsPatch.ChangeLidAction ?? normalizedBaseSettings.ChangeLidAction,
            SuspendMode = settingsPatch.SuspendMode ?? normalizedBaseSettings.SuspendMode,
            PostStopSuspendDelaySeconds = settingsPatch.PostStopSuspendDelaySeconds ?? normalizedBaseSettings.PostStopSuspendDelaySeconds,
            PostStopSuspendSound = settingsPatch.PostStopSuspendSound ?? normalizedBaseSettings.PostStopSuspendSound,
            PostStopSuspendSoundVolumeOverridePercent = settingsPatch.HasPostStopSuspendSoundVolumeOverridePercent ? settingsPatch.PostStopSuspendSoundVolumeOverridePercent : normalizedBaseSettings.PostStopSuspendSoundVolumeOverridePercent,
            ClosedLidStopFollowUpSound = settingsPatch.ClosedLidStopFollowUpSound ?? normalizedBaseSettings.ClosedLidStopFollowUpSound,
            ClosedLidStopFollowUpSoundVolumeOverridePercent = settingsPatch.HasClosedLidStopFollowUpSoundVolumeOverridePercent ? settingsPatch.ClosedLidStopFollowUpSoundVolumeOverridePercent : normalizedBaseSettings.ClosedLidStopFollowUpSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = settingsPatch.HasSuspendHistoryEntryCount ? settingsPatch.SuspendHistoryEntryCount : normalizedBaseSettings.SuspendHistoryEntryCount,
            PreSuspendWebhookUrl = settingsPatch.PreSuspendWebhookUrl ?? normalizedBaseSettings.PreSuspendWebhookUrl,
            PostSessionEndWebhookUrl = settingsPatch.PostSessionEndWebhookUrl ?? normalizedBaseSettings.PostSessionEndWebhookUrl,
            ClosedLidStopFollowUpWebhookUrl = settingsPatch.ClosedLidStopFollowUpWebhookUrl ?? normalizedBaseSettings.ClosedLidStopFollowUpWebhookUrl,
            ClosedLidStopFollowUpDelaySeconds = settingsPatch.ClosedLidStopFollowUpDelaySeconds ?? normalizedBaseSettings.ClosedLidStopFollowUpDelaySeconds,
            RepeatClosedLidStopFollowUp = settingsPatch.RepeatClosedLidStopFollowUp ?? normalizedBaseSettings.RepeatClosedLidStopFollowUp,
            ClosedLidPermissionRequestDecision = settingsPatch.ClosedLidPermissionRequestDecision ?? normalizedBaseSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = settingsPatch.WatchParentProcess ?? normalizedBaseSettings.WatchParentProcess,
            SessionTimeoutMinutes = settingsPatch.HasSessionTimeoutMinutes ? settingsPatch.SessionTimeoutMinutes : normalizedBaseSettings.SessionTimeoutMinutes,
            ServerRuntimeCleanupDelayMinutes = settingsPatch.HasServerRuntimeCleanupDelayMinutes ? settingsPatch.ServerRuntimeCleanupDelayMinutes : normalizedBaseSettings.ServerRuntimeCleanupDelayMinutes,
            EmergencyHibernationOnHighTemperature = settingsPatch.EmergencyHibernationOnHighTemperature ?? normalizedBaseSettings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = settingsPatch.EmergencyHibernationTemperatureMode ?? normalizedBaseSettings.EmergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = settingsPatch.EmergencyHibernationTemperatureCelsius ?? normalizedBaseSettings.EmergencyHibernationTemperatureCelsius
        };
    }

    private static string NormalizePowerRequestReason(string powerRequestReason) => string.IsNullOrWhiteSpace(powerRequestReason) ? PowerRequestOptions.Default.Reason : powerRequestReason;

    private async Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> SendSessionCommandAsync(string commandName, AgentProvider provider, string providerName, string sessionIdentifier, string workingDirectory, int watchedProcessIdentifier, string sessionStateReason, bool isProviderSessionEnd, string sessionEndReason, bool includeStoredSettings, bool startRuntimeIfUnavailable, bool allowRuntimeUnavailableAsSuccess, CancellationToken cancellationToken)
    {
        if (!TryValidateSessionCommandArguments(provider, providerName, sessionIdentifier, out var message)) return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Failure(message);

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var storedSettings, out message)) return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Failure(message);

        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        var normalizedProviderName = AgentProviderDisplay.NormalizeProviderName(provider, providerName);
        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = provider,
            ProviderName = normalizedProviderName,
            SessionIdentifier = sessionIdentifier,
            IsProviderSessionEnd = isProviderSessionEnd,
            SessionEndReason = sessionEndReason ?? string.Empty,
            WatchedProcessIdentifier = watchedProcessIdentifier,
            SessionStateReason = sessionStateReason ?? string.Empty,
            WorkingDirectory = workingDirectory ?? string.Empty,
            HasSettings = includeStoredSettings,
            Settings = normalizedStoredSettings
        };

        var response = await _runtimeClient.SendAsync(request, startRuntimeIfUnavailable, cancellationToken);
        if (!response.Succeeded && !(allowRuntimeUnavailableAsSuccess && response.RuntimeUnavailable)) return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Failure(response.Message);

        var commandOutcome = new LidGuardSessionCommandOutcome
        {
            RequestedCommand = commandName,
            RequestedSessionIdentifier = sessionIdentifier,
            RequestedProvider = provider,
            RequestedProviderName = normalizedProviderName,
            RuntimeMessage = response.Message,
            Snapshot = CreateSnapshot(normalizedStoredSettings, response)
        };
        return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Success(commandOutcome);
    }

    private static LidGuardSessionStatus[] GetMatchingSessions(LidGuardPipeResponse statusResponse, string sessionIdentifier, AgentProvider? provider, string providerName)
    {
        if (!statusResponse.Succeeded) return [];

        var matchingSessions = new List<LidGuardSessionStatus>();
        foreach (var session in statusResponse.Sessions)
        {
            if (!string.Equals(session.SessionIdentifier, sessionIdentifier, StringComparison.Ordinal)) continue;
            if (provider is not null && session.Provider != provider.Value) continue;
            if (!string.IsNullOrWhiteSpace(providerName) && !string.Equals(session.ProviderName, providerName, StringComparison.Ordinal)) continue;
            matchingSessions.Add(session);
        }

        return [.. matchingSessions];
    }

    private static bool TryValidateSessionCommandArguments(AgentProvider provider, string providerName, string sessionIdentifier, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(sessionIdentifier))
        {
            message = "A session identifier is required.";
            return false;
        }

        if (provider != AgentProvider.Mcp) return true;
        if (!string.IsNullOrWhiteSpace(providerName)) return true;

        message = "A provider name is required when provider is mcp.";
        return false;
    }

    private static string[] CreateClosedLidStopFollowUpConfigurationIssueMessages(LidGuardSettings settings)
    {
        var configurationIssues = ClosedLidStopFollowUpConfiguration.GetConfigurationIssues(settings);
        if (configurationIssues.Length == 0) return [];

        var messages = new List<string>();
        foreach (var configurationIssue in configurationIssues)
        {
            var message = configurationIssue.Issue switch
            {
                ClosedLidStopFollowUpConfigurationIssue.ReplyWaitTooShort => "The reply window is too short to notice the push notification and send a reply. Set it to 0 seconds to turn it off, or at least 20 seconds to use replies.",
                ClosedLidStopFollowUpConfigurationIssue.PostStopDelayTooShort => "Sleep or reply waiting can start too early before immediately-following prompts are seen. Set postStopSuspendDelaySeconds to at least 10.",
                _ => configurationIssue.Message
            };
            messages.Add(message);
        }

        return [.. messages.Where(static message => !string.IsNullOrWhiteSpace(message))];
    }

}

using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;
using LidGuard.Ipc;
using LidGuard.Settings;

namespace LidGuard.Control;

public sealed class LidGuardControlService(IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer)
{
    private readonly LidGuardRuntimeClient _runtimeClient = new();

    public async Task<LidGuardOperationResult<LidGuardControlSnapshot>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!LidGuardSettingsStore.TryLoadOrCreate(out var storedSettings, out var message))
            return LidGuardOperationResult<LidGuardControlSnapshot>.Failure(message);

        var response = await _runtimeClient.SendAsync(
            new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status },
            false,
            cancellationToken);

        return LidGuardOperationResult<LidGuardControlSnapshot>.Success(CreateSnapshot(storedSettings, response));
    }

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> ClearSessionSoftLockAsync(
        string sessionIdentifier,
        AgentProvider provider,
        string providerName = "",
        string sessionStateReason = "",
        CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(
            LidGuardPipeCommands.MarkSessionActive,
            provider,
            providerName,
            sessionIdentifier,
            string.Empty,
            0,
            sessionStateReason,
            false,
            false,
            false,
            cancellationToken);

    public async Task<LidGuardOperationResult<LidGuardSessionRemovalOutcome>> RemoveSessionAsync(
        string sessionIdentifier,
        AgentProvider? provider = null,
        string providerName = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier))
            return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure("A session identifier is required.");
        if (provider == AgentProvider.Mcp && string.IsNullOrWhiteSpace(providerName))
            providerName = string.Empty;

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var storedSettings, out var message))
            return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(message);

        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        var normalizedProviderName = provider is null ? string.Empty : AgentProviderDisplay.NormalizeProviderName(provider.Value, providerName);
        var statusResponse = await _runtimeClient.SendAsync(
            new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status },
            false,
            cancellationToken);
        if (!statusResponse.Succeeded && !statusResponse.RuntimeUnavailable)
            return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(statusResponse.Message);

        var removedSessions = GetMatchingSessions(statusResponse, sessionIdentifier, provider, normalizedProviderName);
        var removeResponse = await _runtimeClient.SendAsync(
            new LidGuardPipeRequest
            {
                Command = LidGuardPipeCommands.RemoveSession,
                Provider = provider ?? AgentProvider.Unknown,
                ProviderName = normalizedProviderName,
                SessionIdentifier = sessionIdentifier,
                MatchAllProvidersForSessionIdentifier = provider is null,
                MatchAllProviderNamesForSessionIdentifier = provider == AgentProvider.Mcp && string.IsNullOrWhiteSpace(normalizedProviderName)
            },
            false,
            cancellationToken);
        if (!removeResponse.Succeeded && !removeResponse.RuntimeUnavailable)
            return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(removeResponse.Message);

        return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Success(new LidGuardSessionRemovalOutcome
        {
            RequestedSessionIdentifier = sessionIdentifier,
            HasProviderFilter = provider is not null,
            RequestedProvider = provider ?? AgentProvider.Unknown,
            HasProviderNameFilter = !string.IsNullOrWhiteSpace(normalizedProviderName),
            RequestedProviderName = normalizedProviderName,
            RemovedSessions = removedSessions,
            Snapshot = CreateSnapshot(normalizedStoredSettings, removeResponse)
        });
    }

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> SetSessionSoftLockAsync(
        string sessionIdentifier,
        AgentProvider provider,
        string providerName = "",
        string sessionStateReason = "",
        CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(
            LidGuardPipeCommands.MarkSessionSoftLocked,
            provider,
            providerName,
            sessionIdentifier,
            string.Empty,
            0,
            sessionStateReason,
            false,
            false,
            false,
            cancellationToken);

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> StartSessionAsync(
        string sessionIdentifier,
        AgentProvider provider,
        string providerName = "",
        string workingDirectory = "",
        int watchedProcessIdentifier = 0,
        CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(
            LidGuardPipeCommands.Start,
            provider,
            providerName,
            sessionIdentifier,
            workingDirectory,
            watchedProcessIdentifier,
            string.Empty,
            true,
            true,
            false,
            cancellationToken);

    public Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> StopSessionAsync(
        string sessionIdentifier,
        AgentProvider provider,
        string providerName = "",
        CancellationToken cancellationToken = default)
        => SendSessionCommandAsync(
            LidGuardPipeCommands.Stop,
            provider,
            providerName,
            sessionIdentifier,
            string.Empty,
            0,
            string.Empty,
            false,
            false,
            true,
            cancellationToken);

    public async Task<LidGuardOperationResult<LidGuardSettingsUpdateOutcome>> UpdateSettingsAsync(
        LidGuardSettingsPatch settingsPatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsPatch);
        if (settingsPatch.PostStopSuspendDelaySeconds < 0) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure("Post-stop suspend delay seconds must be a non-negative integer.");
        if (settingsPatch.HasPostStopSuspendSoundVolumeOverridePercent
            && !PostStopSuspendSoundConfiguration.TryValidateVolumeOverridePercent(
                settingsPatch.PostStopSuspendSoundVolumeOverridePercent,
                out var volumeOverrideValidationMessage))
        {
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(volumeOverrideValidationMessage);
        }

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

        var previousStoredSettings = LidGuardSettings.Normalize(currentSettings);
        var updatedStoredSettings = ApplyPatch(previousStoredSettings, settingsPatch);
        if (!PostStopSuspendSoundConfiguration.TryNormalize(
            updatedStoredSettings,
            postStopSuspendSoundPlayer,
            out updatedStoredSettings,
            out message))
        {
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);
        }

        if (settingsPatch.PreSuspendWebhookUrl is not null)
        {
            if (!PreSuspendWebhookConfiguration.TryNormalizeConfiguredValue(
                settingsPatch.PreSuspendWebhookUrl,
                out var normalizedPreSuspendWebhookUrl,
                out message))
            {
                return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);
            }

            updatedStoredSettings = PreSuspendWebhookConfiguration.WithPreSuspendWebhookUrl(
                updatedStoredSettings,
                normalizedPreSuspendWebhookUrl);
        }

        updatedStoredSettings = LidGuardSettings.Normalize(updatedStoredSettings);

        if (!LidGuardSettingsStore.TrySave(updatedStoredSettings, out message)) return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

        var response = await _runtimeClient.SendAsync(
            new LidGuardPipeRequest
            {
                Command = LidGuardPipeCommands.Settings,
                HasSettings = true,
                Settings = updatedStoredSettings
            },
            false,
            cancellationToken);

        var appliedChanges = DescribeChanges(previousStoredSettings, updatedStoredSettings);
        return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Success(new LidGuardSettingsUpdateOutcome
        {
            ResetToDefaults = settingsPatch.ResetToDefaults,
            HadEffectiveChanges = appliedChanges.Length > 0,
            AppliedChanges = appliedChanges,
            PreviousStoredSettings = previousStoredSettings,
            UpdatedStoredSettings = updatedStoredSettings,
            Snapshot = CreateSnapshot(updatedStoredSettings, response)
        });
    }

    private static LidGuardControlSnapshot CreateSnapshot(LidGuardSettings storedSettings, LidGuardPipeResponse response)
    {
        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        if (!response.Succeeded)
        {
            return new LidGuardControlSnapshot
            {
                SettingsFilePath = LidGuardSettingsStore.GetDefaultSettingsFilePath(),
                StoredSettings = normalizedStoredSettings,
                RuntimeReachable = false,
                RuntimeUnavailable = response.RuntimeUnavailable,
                RuntimeMessage = response.Message,
                ActiveSessionCount = response.ActiveSessionCount,
                LidSwitchState = LidSwitchState.Unknown,
                VisibleDisplayMonitorCount = response.VisibleDisplayMonitorCount,
                Sessions = []
            };
        }

        return new LidGuardControlSnapshot
        {
            SettingsFilePath = LidGuardSettingsStore.GetDefaultSettingsFilePath(),
            StoredSettings = normalizedStoredSettings,
            RuntimeReachable = true,
            RuntimeUnavailable = false,
            RuntimeMessage = response.Message,
            HasRuntimeSettings = true,
            RuntimeSettings = LidGuardSettings.Normalize(response.Settings),
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

        return new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = settingsPatch.PreventSystemSleep ?? basePowerRequest.PreventSystemSleep,
                PreventAwayModeSleep = settingsPatch.PreventAwayModeSleep ?? basePowerRequest.PreventAwayModeSleep,
                PreventDisplaySleep = settingsPatch.PreventDisplaySleep ?? basePowerRequest.PreventDisplaySleep,
                Reason = settingsPatch.PowerRequestReason is null
                    ? basePowerRequest.Reason
                    : NormalizePowerRequestReason(settingsPatch.PowerRequestReason)
            },
            ChangeLidAction = settingsPatch.ChangeLidAction ?? normalizedBaseSettings.ChangeLidAction,
            SuspendMode = settingsPatch.SuspendMode ?? normalizedBaseSettings.SuspendMode,
            PostStopSuspendDelaySeconds = settingsPatch.PostStopSuspendDelaySeconds ?? normalizedBaseSettings.PostStopSuspendDelaySeconds,
            PostStopSuspendSound = settingsPatch.PostStopSuspendSound ?? normalizedBaseSettings.PostStopSuspendSound,
            PostStopSuspendSoundVolumeOverridePercent = settingsPatch.HasPostStopSuspendSoundVolumeOverridePercent
                ? settingsPatch.PostStopSuspendSoundVolumeOverridePercent
                : normalizedBaseSettings.PostStopSuspendSoundVolumeOverridePercent,
            PreSuspendWebhookUrl = settingsPatch.PreSuspendWebhookUrl ?? normalizedBaseSettings.PreSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = settingsPatch.ClosedLidPermissionRequestDecision ?? normalizedBaseSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = settingsPatch.WatchParentProcess ?? normalizedBaseSettings.WatchParentProcess,
            EmergencyHibernationOnHighTemperature = settingsPatch.EmergencyHibernationOnHighTemperature ?? normalizedBaseSettings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = settingsPatch.EmergencyHibernationTemperatureMode ?? normalizedBaseSettings.EmergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = settingsPatch.EmergencyHibernationTemperatureCelsius ?? normalizedBaseSettings.EmergencyHibernationTemperatureCelsius
        };
    }

    private static string NormalizePowerRequestReason(string powerRequestReason)
        => string.IsNullOrWhiteSpace(powerRequestReason) ? PowerRequestOptions.Default.Reason : powerRequestReason;

    private async Task<LidGuardOperationResult<LidGuardSessionCommandOutcome>> SendSessionCommandAsync(
        string commandName,
        AgentProvider provider,
        string providerName,
        string sessionIdentifier,
        string workingDirectory,
        int watchedProcessIdentifier,
        string sessionStateReason,
        bool includeStoredSettings,
        bool startRuntimeIfUnavailable,
        bool allowRuntimeUnavailableAsSuccess,
        CancellationToken cancellationToken)
    {
        if (!TryValidateSessionCommandArguments(provider, providerName, sessionIdentifier, out var message))
            return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Failure(message);

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var storedSettings, out message))
            return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Failure(message);

        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        var normalizedProviderName = AgentProviderDisplay.NormalizeProviderName(provider, providerName);
        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = provider,
            ProviderName = normalizedProviderName,
            SessionIdentifier = sessionIdentifier,
            WatchedProcessIdentifier = watchedProcessIdentifier,
            SessionStateReason = sessionStateReason ?? string.Empty,
            WorkingDirectory = workingDirectory ?? string.Empty,
            HasSettings = includeStoredSettings,
            Settings = normalizedStoredSettings
        };

        var response = await _runtimeClient.SendAsync(request, startRuntimeIfUnavailable, cancellationToken);
        if (!response.Succeeded && !(allowRuntimeUnavailableAsSuccess && response.RuntimeUnavailable))
            return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Failure(response.Message);

        return LidGuardOperationResult<LidGuardSessionCommandOutcome>.Success(new LidGuardSessionCommandOutcome
        {
            RequestedCommand = commandName,
            RequestedSessionIdentifier = sessionIdentifier,
            RequestedProvider = provider,
            RequestedProviderName = normalizedProviderName,
            RuntimeMessage = response.Message,
            Snapshot = CreateSnapshot(normalizedStoredSettings, response)
        });
    }

    private static LidGuardSessionStatus[] GetMatchingSessions(
        LidGuardPipeResponse statusResponse,
        string sessionIdentifier,
        AgentProvider? provider,
        string providerName)
    {
        if (!statusResponse.Succeeded) return [];

        var matchingSessions = new List<LidGuardSessionStatus>();
        foreach (var session in statusResponse.Sessions)
        {
            if (!string.Equals(session.SessionIdentifier, sessionIdentifier, StringComparison.Ordinal)) continue;
            if (provider is not null && session.Provider != provider.Value) continue;
            if (!string.IsNullOrWhiteSpace(providerName)
                && !string.Equals(session.ProviderName, providerName, StringComparison.Ordinal)) continue;
            matchingSessions.Add(session);
        }

        return [.. matchingSessions];
    }

    private static bool TryValidateSessionCommandArguments(
        AgentProvider provider,
        string providerName,
        string sessionIdentifier,
        out string message)
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

    private static string[] DescribeChanges(LidGuardSettings previousStoredSettings, LidGuardSettings updatedStoredSettings)
    {
        var previousPowerRequest = previousStoredSettings.PowerRequest ?? PowerRequestOptions.Default;
        var updatedPowerRequest = updatedStoredSettings.PowerRequest ?? PowerRequestOptions.Default;
        var changes = new List<string>();

        AppendChange(changes, previousPowerRequest.PreventSystemSleep, updatedPowerRequest.PreventSystemSleep, "preventSystemSleep");
        AppendChange(changes, previousPowerRequest.PreventAwayModeSleep, updatedPowerRequest.PreventAwayModeSleep, "preventAwayModeSleep");
        AppendChange(changes, previousPowerRequest.PreventDisplaySleep, updatedPowerRequest.PreventDisplaySleep, "preventDisplaySleep");
        AppendChange(changes, previousPowerRequest.Reason, updatedPowerRequest.Reason, "powerRequestReason");
        AppendChange(changes, previousStoredSettings.ChangeLidAction, updatedStoredSettings.ChangeLidAction, "changeLidAction");
        AppendChange(changes, previousStoredSettings.WatchParentProcess, updatedStoredSettings.WatchParentProcess, "watchParentProcess");
        AppendChange(changes, previousStoredSettings.SuspendMode, updatedStoredSettings.SuspendMode, "suspendMode");
        AppendChange(changes, previousStoredSettings.PostStopSuspendDelaySeconds, updatedStoredSettings.PostStopSuspendDelaySeconds, "postStopSuspendDelaySeconds");
        AppendChange(changes, previousStoredSettings.PostStopSuspendSound, updatedStoredSettings.PostStopSuspendSound, "postStopSuspendSound");
        AppendChange(changes, previousStoredSettings.PostStopSuspendSoundVolumeOverridePercent, updatedStoredSettings.PostStopSuspendSoundVolumeOverridePercent, "postStopSuspendSoundVolumeOverridePercent");
        AppendChange(changes, previousStoredSettings.PreSuspendWebhookUrl, updatedStoredSettings.PreSuspendWebhookUrl, "preSuspendWebhookUrl");
        AppendChange(changes, previousStoredSettings.ClosedLidPermissionRequestDecision, updatedStoredSettings.ClosedLidPermissionRequestDecision, "closedLidPermissionRequestDecision");
        AppendChange(changes, previousStoredSettings.EmergencyHibernationOnHighTemperature, updatedStoredSettings.EmergencyHibernationOnHighTemperature, "emergencyHibernationOnHighTemperature");
        AppendChange(changes, previousStoredSettings.EmergencyHibernationTemperatureMode, updatedStoredSettings.EmergencyHibernationTemperatureMode, "emergencyHibernationTemperatureMode");
        AppendChange(changes, previousStoredSettings.EmergencyHibernationTemperatureCelsius, updatedStoredSettings.EmergencyHibernationTemperatureCelsius, "emergencyHibernationTemperatureCelsius");

        return [.. changes];
    }

    private static void AppendChange<TValue>(List<string> changes, TValue previousValue, TValue updatedValue, string changeName)
    {
        if (EqualityComparer<TValue>.Default.Equals(previousValue, updatedValue)) return;
        changes.Add(changeName);
    }
}

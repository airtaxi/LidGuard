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

    public async Task<LidGuardOperationResult<LidGuardSettingsUpdateOutcome>> UpdateSettingsAsync(
        LidGuardSettingsPatch settingsPatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsPatch);
        if (settingsPatch.PostStopSuspendDelaySeconds < 0)
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure("Post-stop suspend delay seconds must be a non-negative integer.");

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var message))
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

        var previousStoredSettings = LidGuardSettings.Normalize(currentSettings);
        var updatedStoredSettings = ApplyPatch(previousStoredSettings, settingsPatch);
        if (!PostStopSuspendSoundConfiguration.TryNormalize(
            updatedStoredSettings,
            postStopSuspendSoundPlayer,
            out updatedStoredSettings,
            out message))
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

        if (!LidGuardSettingsStore.TrySave(updatedStoredSettings, out message))
            return LidGuardOperationResult<LidGuardSettingsUpdateOutcome>.Failure(message);

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

    public async Task<LidGuardOperationResult<LidGuardSessionRemovalOutcome>> RemoveSessionAsync(
        string sessionIdentifier,
        AgentProvider? provider = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier))
            return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure("A session identifier is required.");

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var storedSettings, out var message))
            return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(message);

        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        var statusResponse = await _runtimeClient.SendAsync(
            new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status },
            false,
            cancellationToken);
        if (!statusResponse.Succeeded && !statusResponse.RuntimeUnavailable)
            return LidGuardOperationResult<LidGuardSessionRemovalOutcome>.Failure(statusResponse.Message);

        var removedSessions = GetMatchingSessions(statusResponse, sessionIdentifier, provider);
        var removeResponse = await _runtimeClient.SendAsync(
            new LidGuardPipeRequest
            {
                Command = LidGuardPipeCommands.RemoveSession,
                Provider = provider ?? AgentProvider.Unknown,
                SessionIdentifier = sessionIdentifier,
                MatchAllProvidersForSessionIdentifier = provider is null
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
            RemovedSessions = removedSessions,
            Snapshot = CreateSnapshot(normalizedStoredSettings, removeResponse)
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
            ClosedLidPermissionRequestDecision = settingsPatch.ClosedLidPermissionRequestDecision ?? normalizedBaseSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = settingsPatch.WatchParentProcess ?? normalizedBaseSettings.WatchParentProcess
        };
    }

    private static string NormalizePowerRequestReason(string powerRequestReason)
        => string.IsNullOrWhiteSpace(powerRequestReason) ? PowerRequestOptions.Default.Reason : powerRequestReason;

    private static LidGuardSessionStatus[] GetMatchingSessions(
        LidGuardPipeResponse statusResponse,
        string sessionIdentifier,
        AgentProvider? provider)
    {
        if (!statusResponse.Succeeded) return [];

        var matchingSessions = new List<LidGuardSessionStatus>();
        foreach (var session in statusResponse.Sessions)
        {
            if (!string.Equals(session.SessionIdentifier, sessionIdentifier, StringComparison.Ordinal)) continue;
            if (provider is not null && session.Provider != provider.Value) continue;
            matchingSessions.Add(session);
        }

        return [.. matchingSessions];
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
        AppendChange(changes, previousStoredSettings.ClosedLidPermissionRequestDecision, updatedStoredSettings.ClosedLidPermissionRequestDecision, "closedLidPermissionRequestDecision");

        return [.. changes];
    }

    private static void AppendChange<TValue>(List<string> changes, TValue previousValue, TValue updatedValue, string changeName)
    {
        if (EqualityComparer<TValue>.Default.Equals(previousValue, updatedValue)) return;
        changes.Add(changeName);
    }
}

using System.ComponentModel;
using LidGuard.Mcp.Models;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;
using LidGuard.Control;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LidGuard.Mcp.Tools;

[McpServerToolType]
public sealed class LidGuardSettingsMcpTools(LidGuardControlService controlService)
{
    [McpServerTool(
        Name = "get_settings_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Read the stored LidGuard settings file and, when available, the active LidGuard runtime settings, lid state, and session list.")]
    public async Task<LidGuardSettingsStatusToolResponse> GetSettingsStatus(CancellationToken cancellationToken)
    {
        var result = await controlService.GetStatusAsync(cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return new LidGuardSettingsStatusToolResponse
        {
            Summary = CreateStatusSummary(result.Value),
            Snapshot = result.Value
        };
    }

    [McpServerTool(
        Name = "list_sessions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("List the active LidGuard sessions and runtime lid/session state without returning the full settings payload.")]
    public async Task<LidGuardSessionListToolResponse> ListSessions(CancellationToken cancellationToken)
    {
        var result = await controlService.GetStatusAsync(cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return new LidGuardSessionListToolResponse
        {
            Summary = CreateSessionListSummary(result.Value),
            RuntimeReachable = result.Value.RuntimeReachable,
            RuntimeUnavailable = result.Value.RuntimeUnavailable,
            RuntimeMessage = result.Value.RuntimeMessage,
            ActiveSessionCount = result.Value.ActiveSessionCount,
            LidSwitchState = result.Value.LidSwitchState,
            Sessions = result.Value.Sessions
        };
    }

    [McpServerTool(
        Name = "update_settings",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Update one or more LidGuard settings in a single call, save them to the LidGuard settings file, and try to sync a running runtime without starting one.")]
    public async Task<LidGuardSettingsUpdateToolResponse> UpdateSettings(
        [Description("Reset the starting point to LidGuard's headless runtime defaults before applying any other provided values.")]
        bool resetToDefaults = false,
        [Description("Set whether LidGuard prevents normal system sleep while sessions are active. Omit to keep the current value.")]
        bool? preventSystemSleep = null,
        [Description("Set whether LidGuard requests away mode while sessions are active. Omit to keep the current value.")]
        bool? preventAwayModeSleep = null,
        [Description("Set whether LidGuard prevents display sleep while sessions are active. Omit to keep the current value.")]
        bool? preventDisplaySleep = null,
        [Description("Set whether LidGuard temporarily changes the active power plan lid action to Do Nothing. Omit to keep the current value.")]
        bool? changeLidAction = null,
        [Description("Set whether LidGuard watches the resolved parent process and cleans up when that process exits. Omit to keep the current value.")]
        bool? watchParentProcess = null,
        [Description("Set whether LidGuard should request Emergency Hibernation when the guarded system temperature reaches the configured high-temperature threshold while the lid is closed. Omit to keep the current value.")]
        bool? emergencyHibernationOnHighTemperature = null,
        [Description("Set the Emergency Hibernation temperature threshold in Celsius. Stored and runtime values are clamped to 70 through 110. Omit to keep the current value.")]
        int? emergencyHibernationTemperatureCelsius = null,
        [Description("Set the suspend mode LidGuard uses after the last session stops while the lid is closed. Omit to keep the current value.")]
        SystemSuspendMode? suspendMode = null,
        [Description("Set the post-stop suspend delay in seconds after the last session stops while the lid is closed. Use 0 for immediate suspend. Omit to keep the current value.")]
        int? postStopSuspendDelaySeconds = null,
        [Description("Set the post-stop suspend sound. Use off or an empty string to disable it. Supported system sound names are Asterisk, Beep, Exclamation, Hand, and Question. You can also pass a path to a playable .wav file. Omit to keep the current value.")]
        string postStopSuspendSound = null,
        [Description("Set the webhook URL LidGuard POSTs before requesting suspend. Pass an empty string to disable it. Omit to keep the current value.")]
        string preSuspendWebhookUrl = null,
        [Description("Set the PermissionRequest decision returned while the lid is closed. Omit to keep the current value.")]
        ClosedLidPermissionRequestDecision? closedLidPermissionRequestDecision = null,
        [Description("Set the power request reason string. Pass an empty string to restore LidGuard's default reason text. Omit to keep the current value.")]
        string powerRequestReason = null,
        CancellationToken cancellationToken = default)
    {
        var settingsPatch = new LidGuardSettingsPatch
        {
            ResetToDefaults = resetToDefaults,
            PreventSystemSleep = preventSystemSleep,
            PreventAwayModeSleep = preventAwayModeSleep,
            PreventDisplaySleep = preventDisplaySleep,
            ChangeLidAction = changeLidAction,
            WatchParentProcess = watchParentProcess,
            EmergencyHibernationOnHighTemperature = emergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius,
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
            PostStopSuspendSound = postStopSuspendSound,
            PreSuspendWebhookUrl = preSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = closedLidPermissionRequestDecision,
            PowerRequestReason = powerRequestReason
        };

        var result = await controlService.UpdateSettingsAsync(settingsPatch, cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return new LidGuardSettingsUpdateToolResponse
        {
            Summary = CreateUpdateSummary(result.Value),
            ResetToDefaults = result.Value.ResetToDefaults,
            HadEffectiveChanges = result.Value.HadEffectiveChanges,
            AppliedChanges = result.Value.AppliedChanges,
            PreviousStoredSettings = result.Value.PreviousStoredSettings,
            UpdatedStoredSettings = result.Value.UpdatedStoredSettings,
            Snapshot = result.Value.Snapshot
        };
    }

    [McpServerTool(
        Name = "remove_session",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Remove one or more active LidGuard sessions by session identifier. When provider is omitted, LidGuard removes every active session whose session identifier matches. When provider is mcp, you can also pass providerName to remove only one MCP-backed provider's session.")]
    public async Task<LidGuardSessionRemovalToolResponse> RemoveSession(
        [Description("The session identifier to remove.")]
        string sessionIdentifier,
        [Description("Optional provider filter. Omit to remove matching sessions across all providers.")]
        AgentProvider? provider = null,
        [Description("Optional provider name filter used only when provider is mcp. Omit it to remove every MCP-backed session that shares the same session identifier.")]
        string providerName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.RemoveSessionAsync(sessionIdentifier, provider, providerName, cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return new LidGuardSessionRemovalToolResponse
        {
            Summary = CreateSessionRemovalSummary(result.Value),
            RequestedSessionIdentifier = result.Value.RequestedSessionIdentifier,
            HasProviderFilter = result.Value.HasProviderFilter,
            RequestedProvider = result.Value.RequestedProvider,
            HasProviderNameFilter = result.Value.HasProviderNameFilter,
            RequestedProviderName = result.Value.RequestedProviderName,
            RemovedSessions = result.Value.RemovedSessions,
            Snapshot = result.Value.Snapshot
        };
    }

    [McpServerTool(
        Name = "set_session_soft_lock",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Mark an existing LidGuard session as soft-locked. A soft-locked session stays tracked, but it stops keeping the machine awake. Use this before you finish a turn because you need the user's next input. This tool does not end the turn for you; after calling it, end or hand back the conversation yourself.")]
    public async Task<LidGuardSessionCommandToolResponse> SetSessionSoftLock(
        [Description("The provider whose active LidGuard session should become soft-locked.")]
        AgentProvider provider,
        [Description("The session identifier to soft-lock.")]
        string sessionIdentifier,
        [Description("The reason why the session is becoming soft-locked, such as waiting_for_user_input.")]
        string reason,
        [Description("Required when provider is mcp so LidGuard can distinguish which MCP-backed provider owns the session.")]
        string providerName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.SetSessionSoftLockAsync(sessionIdentifier, provider, providerName, reason, cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return CreateSessionCommandToolResponse(result.Value);
    }

    [McpServerTool(
        Name = "clear_session_soft_lock",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true),
     Description("Clear a previous soft lock when autonomous work resumes on an existing LidGuard session. Call this before continuing work on the same session after the user replies or the waiting condition is resolved.")]
    public async Task<LidGuardSessionCommandToolResponse> ClearSessionSoftLock(
        [Description("The provider whose active LidGuard session should become active again.")]
        AgentProvider provider,
        [Description("The session identifier whose soft lock should be cleared.")]
        string sessionIdentifier,
        [Description("Optional reason describing why the session is active again, such as resumed_after_user_reply.")]
        string reason = null,
        [Description("Required when provider is mcp so LidGuard can distinguish which MCP-backed provider owns the session.")]
        string providerName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await controlService.ClearSessionSoftLockAsync(sessionIdentifier, provider, providerName, reason, cancellationToken);
        if (!result.Succeeded) throw new McpException(result.Message);

        return CreateSessionCommandToolResponse(result.Value);
    }

    private static string CreateStatusSummary(LidGuardControlSnapshot snapshot)
    {
        if (snapshot.RuntimeReachable) return $"Stored settings loaded. Runtime reachable with {snapshot.ActiveSessionCount} active session(s).";
        if (snapshot.RuntimeUnavailable) return "Stored settings loaded. LidGuard runtime is not running.";
        return $"Stored settings loaded. Runtime status query failed: {snapshot.RuntimeMessage}";
    }

    private static string CreateSessionListSummary(LidGuardControlSnapshot snapshot)
    {
        if (snapshot.RuntimeReachable) return $"Runtime reachable with {snapshot.ActiveSessionCount} active session(s).";
        if (snapshot.RuntimeUnavailable) return "LidGuard runtime is not running, so there are no active sessions to list.";
        return $"Runtime session query failed: {snapshot.RuntimeMessage}";
    }

    private static string CreateUpdateSummary(LidGuardSettingsUpdateOutcome outcome)
    {
        var changeSummary = outcome.HadEffectiveChanges
            ? $"Applied {outcome.AppliedChanges.Length} setting change(s): {string.Join(", ", outcome.AppliedChanges)}."
            : "No setting values changed.";

        if (outcome.Snapshot.RuntimeReachable) return $"{changeSummary} Running runtime synchronized.";
        if (outcome.Snapshot.RuntimeUnavailable) return $"{changeSummary} Runtime is not running, so the saved settings will apply on the next session start.";
        return $"{changeSummary} Saved settings, but runtime synchronization failed: {outcome.Snapshot.RuntimeMessage}";
    }

    private static string CreateSessionRemovalSummary(LidGuardSessionRemovalOutcome outcome)
    {
        var requestScope = outcome.HasProviderFilter
            ? $"{AgentProviderDisplay.CreateProviderDisplayText(outcome.RequestedProvider, outcome.RequestedProviderName)}:{outcome.RequestedSessionIdentifier}"
            : $"session id {outcome.RequestedSessionIdentifier}";
        var removedSessionCount = outcome.RemovedSessions.Length;
        var removalSummary = removedSessionCount == 0
            ? $"No active sessions matched {requestScope}."
            : $"Removed {removedSessionCount} active session(s) matching {requestScope}.";

        if (outcome.Snapshot.RuntimeReachable) return $"{removalSummary} Runtime now has {outcome.Snapshot.ActiveSessionCount} active session(s).";
        if (outcome.Snapshot.RuntimeUnavailable) return $"{removalSummary} Runtime is not running.";
        return $"{removalSummary} Runtime status after removal is unavailable: {outcome.Snapshot.RuntimeMessage}";
    }

    private static LidGuardSessionCommandToolResponse CreateSessionCommandToolResponse(LidGuardSessionCommandOutcome outcome)
    {
        return new LidGuardSessionCommandToolResponse
        {
            Summary = CreateSessionCommandSummary(outcome),
            RequestedCommand = outcome.RequestedCommand,
            RequestedSessionIdentifier = outcome.RequestedSessionIdentifier,
            RequestedProvider = outcome.RequestedProvider,
            RequestedProviderName = outcome.RequestedProviderName,
            Snapshot = outcome.Snapshot
        };
    }

    private static string CreateSessionCommandSummary(LidGuardSessionCommandOutcome outcome)
    {
        var scope = $"{AgentProviderDisplay.CreateProviderDisplayText(outcome.RequestedProvider, outcome.RequestedProviderName)}:{outcome.RequestedSessionIdentifier}";
        if (outcome.Snapshot.RuntimeReachable)
            return $"{outcome.RuntimeMessage} Runtime now tracks {outcome.Snapshot.ActiveSessionCount} active session(s) after handling {scope}.";
        if (outcome.Snapshot.RuntimeUnavailable)
            return $"{outcome.RuntimeMessage} Runtime is not running after handling {scope}.";
        return $"{outcome.RuntimeMessage} Runtime status after handling {scope} is unavailable: {outcome.Snapshot.RuntimeMessage}";
    }
}

using System.ComponentModel;
using LidGuard.Mcp.Models;
using LidGuardLib.Commons.Power;
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
        [Description("Set the suspend mode LidGuard uses after the last session stops while the lid is closed. Omit to keep the current value.")]
        SystemSuspendMode? suspendMode = null,
        [Description("Set the post-stop suspend delay in seconds after the last session stops while the lid is closed. Use 0 for immediate suspend. Omit to keep the current value.")]
        int? postStopSuspendDelaySeconds = null,
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
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
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

    private static string CreateStatusSummary(LidGuardControlSnapshot snapshot)
    {
        if (snapshot.RuntimeReachable) return $"Stored settings loaded. Runtime reachable with {snapshot.ActiveSessionCount} active session(s).";
        if (snapshot.RuntimeUnavailable) return "Stored settings loaded. LidGuard runtime is not running.";
        return $"Stored settings loaded. Runtime status query failed: {snapshot.RuntimeMessage}";
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
}

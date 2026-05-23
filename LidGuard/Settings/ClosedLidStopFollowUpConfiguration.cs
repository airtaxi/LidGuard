namespace LidGuard.Settings;

internal static class ClosedLidStopFollowUpConfiguration
{
    public const string FeatureStateOff = "Off";
    public const string FeatureStateOn = "On";
    public const string FeatureStateConfigurationError = "ConfigurationError";
    public const int DefaultHookTimeoutSeconds = 30;
    public const int MaximumReplyExtensionSeconds = 300;
    public const int MinimumReplyWaitSeconds = 20;
    public const int MinimumPostStopSuspendDelaySeconds = 10;

    public static string GetDisplayValue(string closedLidStopFollowUpWebhookUrl)
        => WebhookUrlConfiguration.GetDisplayValue(closedLidStopFollowUpWebhookUrl);

    public static string GetFeatureState(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (string.IsNullOrWhiteSpace(normalizedSettings.ClosedLidStopFollowUpWebhookUrl)) return FeatureStateOff;
        if (GetConfigurationIssues(normalizedSettings).Length > 0) return FeatureStateConfigurationError;
        if (normalizedSettings.ClosedLidStopFollowUpDelaySeconds == 0) return FeatureStateOff;
        return FeatureStateOn;
    }

    public static int GetManagedHookTimeoutSeconds(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (!IsEnabled(normalizedSettings, out _)) return DefaultHookTimeoutSeconds;
        return Math.Max(DefaultHookTimeoutSeconds, normalizedSettings.PostStopSuspendDelaySeconds + normalizedSettings.ClosedLidStopFollowUpDelaySeconds + MaximumReplyExtensionSeconds);
    }

    public static bool IsEnabled(LidGuardSettings settings, out string normalizedClosedLidStopFollowUpWebhookUrl)
    {
        normalizedClosedLidStopFollowUpWebhookUrl = string.Empty;
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (normalizedSettings.ClosedLidStopFollowUpDelaySeconds == 0) return false;
        if (GetConfigurationIssues(normalizedSettings).Length > 0) return false;
        if (!TryNormalizeConfiguredValue(normalizedSettings.ClosedLidStopFollowUpWebhookUrl, out normalizedClosedLidStopFollowUpWebhookUrl, out _))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(normalizedClosedLidStopFollowUpWebhookUrl);
    }

    public static ClosedLidStopFollowUpConfigurationIssueDetail[] GetConfigurationIssues(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (string.IsNullOrWhiteSpace(normalizedSettings.ClosedLidStopFollowUpWebhookUrl)) return [];

        var issues = new List<ClosedLidStopFollowUpConfigurationIssueDetail>();
        if (!TryNormalizeConfiguredValue(normalizedSettings.ClosedLidStopFollowUpWebhookUrl, out _, out var webhookUrlMessage))
        {
            issues.Add(new ClosedLidStopFollowUpConfigurationIssueDetail(ClosedLidStopFollowUpConfigurationIssue.InvalidWebhookUrl, webhookUrlMessage));
        }

        if (normalizedSettings.ClosedLidStopFollowUpDelaySeconds is > 0 and < MinimumReplyWaitSeconds)
        {
            issues.Add(new ClosedLidStopFollowUpConfigurationIssueDetail(ClosedLidStopFollowUpConfigurationIssue.ReplyWaitTooShort, string.Empty));
        }

        if (normalizedSettings.ClosedLidStopFollowUpDelaySeconds > 0 && normalizedSettings.PostStopSuspendDelaySeconds < MinimumPostStopSuspendDelaySeconds)
        {
            issues.Add(new ClosedLidStopFollowUpConfigurationIssueDetail(ClosedLidStopFollowUpConfigurationIssue.PostStopDelayTooShort, string.Empty));
        }

        return [.. issues];
    }

    public static bool TryNormalizeConfiguredValue(string closedLidStopFollowUpWebhookUrl, out string normalizedClosedLidStopFollowUpWebhookUrl, out string message)
        => WebhookUrlConfiguration.TryNormalizeConfiguredValue(closedLidStopFollowUpWebhookUrl, "closed-lid stop follow-up", out normalizedClosedLidStopFollowUpWebhookUrl, out message);
}

internal enum ClosedLidStopFollowUpConfigurationIssue
{
    InvalidWebhookUrl,
    ReplyWaitTooShort,
    PostStopDelayTooShort
}

internal readonly record struct ClosedLidStopFollowUpConfigurationIssueDetail(ClosedLidStopFollowUpConfigurationIssue Issue, string Message);

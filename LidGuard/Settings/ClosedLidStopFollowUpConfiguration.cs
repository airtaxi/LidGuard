namespace LidGuard.Settings;

internal static class ClosedLidStopFollowUpConfiguration
{
    public const string FeatureStateOff = "Off";
    public const string FeatureStateOn = "On";
    public const string FeatureStateConfigurationError = "ConfigurationError";
    public const int DefaultHookTimeoutSeconds = 30;
    public const int AdditionalHookTimeoutBufferSeconds = 15;

    public static string GetDisplayValue(string closedLidStopFollowUpWebhookUrl)
        => WebhookUrlConfiguration.GetDisplayValue(closedLidStopFollowUpWebhookUrl);

    public static string GetFeatureState(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (normalizedSettings.PostStopSuspendDelaySeconds <= 0) return FeatureStateOff;
        if (string.IsNullOrWhiteSpace(normalizedSettings.ClosedLidStopFollowUpWebhookUrl)) return FeatureStateOff;
        return TryNormalizeConfiguredValue(normalizedSettings.ClosedLidStopFollowUpWebhookUrl, out _, out _)
            ? FeatureStateOn
            : FeatureStateConfigurationError;
    }

    public static int GetManagedHookTimeoutSeconds(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (!IsEnabled(normalizedSettings, out _)) return DefaultHookTimeoutSeconds;
        return Math.Max(
            DefaultHookTimeoutSeconds,
            normalizedSettings.PostStopSuspendDelaySeconds + AdditionalHookTimeoutBufferSeconds);
    }

    public static bool IsEnabled(LidGuardSettings settings, out string normalizedClosedLidStopFollowUpWebhookUrl)
    {
        normalizedClosedLidStopFollowUpWebhookUrl = string.Empty;
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (normalizedSettings.PostStopSuspendDelaySeconds <= 0) return false;
        if (!TryNormalizeConfiguredValue(
            normalizedSettings.ClosedLidStopFollowUpWebhookUrl,
            out normalizedClosedLidStopFollowUpWebhookUrl,
            out _))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(normalizedClosedLidStopFollowUpWebhookUrl);
    }

    public static bool TryNormalizeConfiguredValue(
        string closedLidStopFollowUpWebhookUrl,
        out string normalizedClosedLidStopFollowUpWebhookUrl,
        out string message)
        => WebhookUrlConfiguration.TryNormalizeConfiguredValue(
            closedLidStopFollowUpWebhookUrl,
            "closed-lid stop follow-up",
            out normalizedClosedLidStopFollowUpWebhookUrl,
            out message);
}

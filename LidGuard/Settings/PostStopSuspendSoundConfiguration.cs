using LidGuard.Services;

namespace LidGuard.Settings;

internal static class PostStopSuspendSoundConfiguration
{
    public static string GetDisplayValue(string postStopSuspendSound)
        => string.IsNullOrWhiteSpace(postStopSuspendSound) ? "off" : postStopSuspendSound;

    public static string GetVolumeOverrideDisplayValue(int? postStopSuspendSoundVolumeOverridePercent)
        => postStopSuspendSoundVolumeOverridePercent is null ? "off" : $"{postStopSuspendSoundVolumeOverridePercent}%";

    public static bool TryValidateVolumeOverridePercent(int? postStopSuspendSoundVolumeOverridePercent, out string message)
        => TryValidateVolumeOverridePercent(postStopSuspendSoundVolumeOverridePercent, "Post-stop suspend sound", out message);

    public static bool TryValidateClosedLidStopFollowUpVolumeOverridePercent(int? closedLidStopFollowUpSoundVolumeOverridePercent, out string message)
        => TryValidateVolumeOverridePercent(closedLidStopFollowUpSoundVolumeOverridePercent, "Closed-lid stop follow-up sound", out message);

    public static bool TryValidateVolumeOverridePercent(int? soundVolumeOverridePercent, string soundDescription, out string message)
    {
        message = string.Empty;
        if (LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(soundVolumeOverridePercent)) return true;

        message =
            $"{soundDescription} volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.";
        return false;
    }

    public static bool TryNormalize(LidGuardSettings settings, IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer, out LidGuardSettings normalizedSettings, out string message)
    {
        var normalizedInputSettings = LidGuardSettings.Normalize(settings);
        var postStopSuspendSoundNormalizeResult = postStopSuspendSoundPlayer.NormalizeConfiguration(normalizedInputSettings.PostStopSuspendSound);
        if (!postStopSuspendSoundNormalizeResult.Succeeded)
        {
            normalizedSettings = normalizedInputSettings;
            message = postStopSuspendSoundNormalizeResult.Message;
            return false;
        }

        var closedLidStopFollowUpSoundNormalizeResult = postStopSuspendSoundPlayer.NormalizeConfiguration(normalizedInputSettings.ClosedLidStopFollowUpSound);
        if (!closedLidStopFollowUpSoundNormalizeResult.Succeeded)
        {
            normalizedSettings = normalizedInputSettings;
            message = closedLidStopFollowUpSoundNormalizeResult.Message;
            return false;
        }

        normalizedSettings = normalizedInputSettings with
        {
            PostStopSuspendSound = postStopSuspendSoundNormalizeResult.Value,
            ClosedLidStopFollowUpSound = closedLidStopFollowUpSoundNormalizeResult.Value
        };

        message = string.Empty;
        return true;
    }
}

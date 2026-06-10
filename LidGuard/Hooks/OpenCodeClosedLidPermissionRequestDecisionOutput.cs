using System.Text.Json;
using System.Text.Json.Serialization;
using LidGuard.Ipc;
using LidGuard.Settings;

namespace LidGuard.Hooks;

internal static class OpenCodeClosedLidPermissionRequestDecisionOutput
{
    private const string DenyMessage = "LidGuard denied this permission request because the lid is closed "
        + "and ClosedLidPermissionRequestDecision is set to Deny. To allow future closed-lid permission requests, "
        + "run: lidguard settings --closed-lid-permission-request-decision allow.";

    public static int Write(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        var decision = normalizedSettings.ClosedLidPermissionRequestDecision;
        if (decision == ClosedLidPermissionRequestDecision.Ask) return 0;

        var statusText = decision == ClosedLidPermissionRequestDecision.Allow ? "allow" : "deny";
        var output = new OpenCodePermissionAskDecisionOutput
        {
            Status = statusText,
            Message = decision == ClosedLidPermissionRequestDecision.Deny ? DenyMessage : null
        };

        Console.WriteLine(JsonSerializer.Serialize(output, LidGuardJsonSerializerContext.Default.OpenCodePermissionAskDecisionOutput));
        return 0;
    }
}

internal sealed class OpenCodePermissionAskDecisionOutput
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Message { get; init; }
}

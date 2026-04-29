using System.Text.Json.Nodes;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Hooks;

internal static class CodexClosedLidPermissionRequestDecisionOutput
{
    private const string DenyMessage = "LidGuard denied this permission request because the lid is closed "
        + "and ClosedLidPermissionRequestDecision is set to Deny. To allow future closed-lid permission requests, "
        + "run: lidguard settings --closed-lid-permission-request-decision allow.";
    private const string PermissionRequestHookEventName = "PermissionRequest";

    public static int Write(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        var decision = normalizedSettings.ClosedLidPermissionRequestDecision;
        var behaviorText = decision == ClosedLidPermissionRequestDecision.Allow ? "allow" : "deny";
        var decisionObject = new JsonObject
        {
            ["behavior"] = behaviorText
        };

        if (decision == ClosedLidPermissionRequestDecision.Deny) decisionObject["message"] = DenyMessage;

        var outputObject = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = PermissionRequestHookEventName,
                ["decision"] = decisionObject
            }
        };

        Console.WriteLine(outputObject.ToJsonString());
        return 0;
    }
}

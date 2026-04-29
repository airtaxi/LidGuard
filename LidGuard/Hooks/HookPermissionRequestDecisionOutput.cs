using System.Text.Json.Nodes;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Hooks;

internal static class HookPermissionRequestDecisionOutput
{
    private const string DenyMessage = "LidGuard denied this permission request because PermissionRequest behavior is set to Deny. "
        + "To allow future permission requests, run: lidguard settings --permission-request-behavior allow.";
    private const string PermissionRequestHookEventName = "PermissionRequest";

    public static int Write(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        var behaviorText = normalizedSettings.PermissionRequestBehavior == HookPermissionRequestBehavior.Allow ? "allow" : "deny";
        var decisionObject = new JsonObject
        {
            ["behavior"] = behaviorText
        };

        if (normalizedSettings.PermissionRequestBehavior == HookPermissionRequestBehavior.Deny) decisionObject["message"] = DenyMessage;

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

using System.Text.Json.Nodes;
using LidGuard.Settings;

namespace LidGuard.Hooks;

internal static class GitHubCopilotClosedLidPermissionRequestDecisionOutput
{
    private const string DenyMessage = "LidGuard denied this permission request because the lid is closed "
        + "and ClosedLidPermissionRequestDecision is set to Deny. To allow future closed-lid permission requests, "
        + "run: lidguard settings --closed-lid-permission-request-decision allow.";
    private const bool InterruptInteractivePermissionPath = true;

    public static int Write(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        var decision = normalizedSettings.ClosedLidPermissionRequestDecision;
        var behaviorText = decision == ClosedLidPermissionRequestDecision.Allow ? "allow" : "deny";
        var outputObject = new JsonObject
        {
            ["behavior"] = behaviorText,
            ["interrupt"] = InterruptInteractivePermissionPath
        };

        if (decision == ClosedLidPermissionRequestDecision.Deny) outputObject["message"] = DenyMessage;

        Console.WriteLine(outputObject.ToJsonString());
        return 0;
    }
}

using System.Text.Json.Nodes;

namespace LidGuard.Hooks;

internal static class GitHubCopilotClosedLidAskUserPreToolUseOutput
{
    private const string DenyMessage = "LidGuard denied this ask_user request because the lid is closed.";

    public static int Write()
    {
        var outputObject = new JsonObject
        {
            ["permissionDecision"] = "deny",
            ["permissionDecisionReason"] = DenyMessage
        };

        Console.WriteLine(outputObject.ToJsonString());
        return 0;
    }
}

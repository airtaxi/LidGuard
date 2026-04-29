using System.Text.Json.Nodes;

namespace LidGuard.Hooks;

internal static class ClaudeClosedLidElicitationOutput
{
    private const string CancelReason = "LidGuard canceled this elicitation because the lid is closed.";
    private const string ElicitationHookEventName = "Elicitation";

    public static int Write()
    {
        var outputObject = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = ElicitationHookEventName,
                ["action"] = "cancel"
            },
            ["reason"] = CancelReason
        };

        Console.WriteLine(outputObject.ToJsonString());
        return 0;
    }
}

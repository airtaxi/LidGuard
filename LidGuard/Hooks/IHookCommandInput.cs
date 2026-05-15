namespace LidGuard.Hooks;

public interface IHookCommandInput
{
    string SessionIdentifier { get; }

    string TranscriptPath { get; }

    string WorkingDirectory { get; }

    string Prompt { get; }
}

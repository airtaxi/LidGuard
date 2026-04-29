namespace LidGuardLib.Commons.Sessions;

public readonly record struct LidGuardSessionKey(AgentProvider Provider, string SessionIdentifier)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(SessionIdentifier);

    public override string ToString() => $"{Provider}:{SessionIdentifier}";
}

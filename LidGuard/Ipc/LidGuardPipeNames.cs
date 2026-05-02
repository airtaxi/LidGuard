namespace LidGuard.Ipc;

internal static class LidGuardPipeNames
{
    public static string RuntimeMutexName => OperatingSystem.IsWindows() ? @"Local\LidGuard.Runtime.v1" : "LidGuard.Runtime.v1";

    public const string RuntimePipeName = "LidGuard.Runtime.v1";
}


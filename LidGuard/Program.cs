using LidGuard.Commands;
using LidGuard.Diagnostics;

namespace LidGuard;

internal static class Program
{
    public static Task<int> Main(string[] commandLineArguments)
    {
        LidGuardExceptionLog.SubscribeGlobalHandlers();
        return LidGuardCommandLineApplication.RunAsync(commandLineArguments);
    }
}


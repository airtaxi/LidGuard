using LidGuard.Commands;

namespace LidGuard;

internal static class Program
{
    public static Task<int> Main(string[] commandLineArguments) => LidGuardCommandLineApplication.RunAsync(commandLineArguments);
}


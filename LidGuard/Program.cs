using LidGuard.Commands;
using LidGuard.Diagnostics;
using LidGuard.Localization;

namespace LidGuard;

internal static class Program
{
    public static Task<int> Main(string[] commandLineArguments)
    {
        LidGuardExceptionLog.SubscribeGlobalHandlers();
        LidGuardCulture.ApplyEffectiveCultureFromEnvironmentOrSettings();
        return LidGuardCommandLineApplication.RunAsync(commandLineArguments);
    }
}


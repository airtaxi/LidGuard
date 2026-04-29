using LidGuard.Control;
using LidGuard.Mcp.Tools;
using LidGuardLib.Windows.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace LidGuard.Mcp;

internal static class ProviderMcpServerCommand
{
    public const string CommandName = "provider-mcp-server";

    public static async Task<int> RunAsync(string[] commandLineArguments)
    {
        if (!TryGetProviderName(commandLineArguments, out var providerName, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var runtimePlatform = new WindowsLidGuardRuntimePlatform();
        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        var applicationBuilder = Host.CreateApplicationBuilder(commandLineArguments);
        applicationBuilder.Logging.ClearProviders();
        applicationBuilder.Logging.AddConsole(consoleLoggerOptions =>
        {
            consoleLoggerOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        applicationBuilder.Services.AddSingleton(postStopSuspendSoundPlayerResult.Value);
        applicationBuilder.Services.AddSingleton(new ProviderMcpServerConfiguration { ProviderName = providerName });
        applicationBuilder.Services.AddSingleton<LidGuardControlService>();
        applicationBuilder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<LidGuardProviderMcpTools>();

        await applicationBuilder.Build().RunAsync();
        return 0;
    }

    private static bool TryGetProviderName(string[] commandLineArguments, out string providerName, out string message)
    {
        providerName = string.Empty;
        message = string.Empty;

        for (var argumentIndex = 0; argumentIndex < commandLineArguments.Length; argumentIndex++)
        {
            var argument = commandLineArguments[argumentIndex];
            if (argument.StartsWith("--provider-name=", StringComparison.OrdinalIgnoreCase))
            {
                providerName = argument["--provider-name=".Length..].Trim();
                break;
            }

            if (!argument.Equals("--provider-name", StringComparison.OrdinalIgnoreCase)) continue;
            if (argumentIndex + 1 >= commandLineArguments.Length)
            {
                message = "A value is required after --provider-name.";
                return false;
            }

            providerName = commandLineArguments[argumentIndex + 1].Trim();
            break;
        }

        if (!string.IsNullOrWhiteSpace(providerName)) return true;

        message = "The provider-mcp-server command requires --provider-name <name>.";
        return false;
    }
}

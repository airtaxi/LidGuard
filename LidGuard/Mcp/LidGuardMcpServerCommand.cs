using LidGuard.Control;
using LidGuard.Mcp.Tools;
using LidGuard.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace LidGuard.Mcp;

internal static class LidGuardMcpServerCommand
{
    public const string CommandName = "mcp-server";

    public static async Task<int> RunAsync(string[] commandLineArguments)
    {
        var runtimePlatform = new LidGuardRuntimePlatform();
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

        var toolSerializerOptions = LidGuardMcpJsonUtilities.CreateToolSerializerOptions();

        applicationBuilder.Services.AddSingleton(postStopSuspendSoundPlayerResult.Value);
        applicationBuilder.Services.AddSingleton<LidGuardControlService>();
        applicationBuilder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<LidGuardSettingsMcpTools>(toolSerializerOptions);

        await applicationBuilder.Build().RunAsync();
        return 0;
    }
}

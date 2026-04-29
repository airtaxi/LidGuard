using LidGuard.Control;
using LidGuard.Mcp.Tools;
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
        var applicationBuilder = Host.CreateApplicationBuilder(commandLineArguments);
        applicationBuilder.Logging.ClearProviders();
        applicationBuilder.Logging.AddConsole(consoleLoggerOptions =>
        {
            consoleLoggerOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        applicationBuilder.Services.AddSingleton<LidGuardControlService>();
        applicationBuilder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<LidGuardSettingsMcpTools>();

        await applicationBuilder.Build().RunAsync();
        return 0;
    }
}

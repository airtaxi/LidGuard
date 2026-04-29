using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace LidGuard.Ipc;

internal sealed class LidGuardRuntimeClient
{
    private static readonly TimeSpan s_runtimeConnectionTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan s_runtimeStartupTimeout = TimeSpan.FromSeconds(5);

    public async Task<LidGuardPipeResponse> SendAsync(LidGuardPipeRequest request, bool startRuntimeIfUnavailable)
    {
        var pipeClientStream = await WaitForRuntimeAsync(s_runtimeConnectionTimeout);
        if (pipeClientStream is null && startRuntimeIfUnavailable)
        {
            if (!TryStartRuntime()) return LidGuardPipeResponse.Failure("Failed to start the LidGuard runtime.", runtimeUnavailable: true);
            pipeClientStream = await WaitForRuntimeAsync(s_runtimeStartupTimeout);
        }

        if (pipeClientStream is null) return LidGuardPipeResponse.Failure("LidGuard runtime is not running.", runtimeUnavailable: true);

        using (pipeClientStream)
        {
            return await SendConnectedAsync(pipeClientStream, request);
        }
    }

    private static async Task<LidGuardPipeResponse> SendConnectedAsync(Stream stream, LidGuardPipeRequest request)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        var requestJson = JsonSerializer.Serialize(request, LidGuardJsonSerializerContext.Default.LidGuardPipeRequest);
        await streamWriter.WriteLineAsync(requestJson);

        var responseJson = await streamReader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(responseJson)) return LidGuardPipeResponse.Failure("The LidGuard runtime returned an empty response.");

        try
        {
            var response = JsonSerializer.Deserialize(responseJson, LidGuardJsonSerializerContext.Default.LidGuardPipeResponse);
            return response ?? LidGuardPipeResponse.Failure("The LidGuard runtime response could not be parsed.");
        }
        catch (JsonException exception) { return LidGuardPipeResponse.Failure($"The LidGuard runtime returned invalid JSON: {exception.Message}"); }
    }

    private static async Task<NamedPipeClientStream> WaitForRuntimeAsync(TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < stopAt)
        {
            var pipeClientStream = await TryConnectAsync();
            if (pipeClientStream is not null) return pipeClientStream;
            await Task.Delay(100);
        }

        return null;
    }

    private static async Task<NamedPipeClientStream> TryConnectAsync()
    {
        var pipeClientStream = new NamedPipeClientStream(
            ".",
            LidGuardPipeNames.RuntimePipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipeClientStream.ConnectAsync(250);
            return pipeClientStream;
        }
        catch (TimeoutException)
        {
            pipeClientStream.Dispose();
            return null;
        }
        catch (IOException)
        {
            pipeClientStream.Dispose();
            return null;
        }
    }

    private static bool TryStartRuntime()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };

        processStartInfo.Arguments = LidGuardPipeCommands.RunServer;

        try
        {
            using var process = Process.Start(processStartInfo);
            return process is not null;
        }
        catch { return false; }
    }
}


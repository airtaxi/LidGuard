using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace LidGuard.Ipc;

internal sealed class LidGuardRuntimeClient
{
    private static readonly TimeSpan s_runtimeConnectionTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan s_runtimeStartupTimeout = TimeSpan.FromSeconds(5);

    public async Task<LidGuardPipeResponse> SendAsync(
        LidGuardPipeRequest request,
        bool startRuntimeIfUnavailable,
        CancellationToken cancellationToken = default)
    {
        var pipeClientStream = await WaitForRuntimeAsync(s_runtimeConnectionTimeout, cancellationToken);
        if (pipeClientStream is null && startRuntimeIfUnavailable)
        {
            if (!TryStartRuntime()) return LidGuardPipeResponse.Failure("Failed to start the LidGuard runtime.", runtimeUnavailable: true);
            pipeClientStream = await WaitForRuntimeAsync(s_runtimeStartupTimeout, cancellationToken);
        }

        if (pipeClientStream is null) return LidGuardPipeResponse.Failure("LidGuard runtime is not running.", runtimeUnavailable: true);

        using (pipeClientStream)
        {
            return await SendConnectedAsync(pipeClientStream, request, cancellationToken);
        }
    }

    private static async Task<LidGuardPipeResponse> SendConnectedAsync(Stream stream, LidGuardPipeRequest request, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        var requestJson = JsonSerializer.Serialize(request, LidGuardJsonSerializerContext.Default.LidGuardPipeRequest);
        await streamWriter.WriteLineAsync(requestJson.AsMemory(), cancellationToken);

        var responseJson = await streamReader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson)) return LidGuardPipeResponse.Failure("The LidGuard runtime returned an empty response.");

        try
        {
            var response = JsonSerializer.Deserialize(responseJson, LidGuardJsonSerializerContext.Default.LidGuardPipeResponse);
            return response ?? LidGuardPipeResponse.Failure("The LidGuard runtime response could not be parsed.");
        }
        catch (JsonException exception) { return LidGuardPipeResponse.Failure($"The LidGuard runtime returned invalid JSON: {exception.Message}"); }
    }

    private static async Task<NamedPipeClientStream> WaitForRuntimeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopAt = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < stopAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pipeClientStream = await TryConnectAsync(cancellationToken);
            if (pipeClientStream is not null) return pipeClientStream;
            await Task.Delay(100, cancellationToken);
        }

        return null;
    }

    private static async Task<NamedPipeClientStream> TryConnectAsync(CancellationToken cancellationToken)
    {
        var pipeClientStream = new NamedPipeClientStream(
            ".",
            LidGuardPipeNames.RuntimePipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipeClientStream.ConnectAsync(250, cancellationToken);
            return pipeClientStream;
        }
        catch (OperationCanceledException)
        {
            pipeClientStream.Dispose();
            throw;
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
        var runtimeExecutablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(runtimeExecutablePath)) return false;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = runtimeExecutablePath,
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

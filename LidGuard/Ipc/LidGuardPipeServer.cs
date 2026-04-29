using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LidGuard.Runtime;

namespace LidGuard.Ipc;

internal sealed class LidGuardPipeServer(LidGuardRuntimeCoordinator runtimeCoordinator)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipeServerStream = new NamedPipeServerStream(
                LidGuardPipeNames.RuntimePipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipeServerStream.WaitForConnectionAsync(cancellationToken);
            await HandleConnectionAsync(pipeServerStream, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        var requestJson = await streamReader.ReadLineAsync(cancellationToken);
        var response = await CreateResponseAsync(requestJson, cancellationToken);
        var responseJson = JsonSerializer.Serialize(response, LidGuardJsonSerializerContext.Default.LidGuardPipeResponse);
        await streamWriter.WriteLineAsync(responseJson);
    }

    private async Task<LidGuardPipeResponse> CreateResponseAsync(string requestJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestJson)) return LidGuardPipeResponse.Failure("The runtime received an empty request.");

        try
        {
            var request = JsonSerializer.Deserialize(requestJson, LidGuardJsonSerializerContext.Default.LidGuardPipeRequest);
            if (request is null) return LidGuardPipeResponse.Failure("The runtime could not parse the request.");
            return await runtimeCoordinator.HandleAsync(request, cancellationToken);
        }
        catch (JsonException exception) { return LidGuardPipeResponse.Failure($"The runtime received invalid JSON: {exception.Message}"); }
    }
}


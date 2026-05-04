using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LidGuard.Runtime;

namespace LidGuard.Ipc;

internal sealed class LidGuardPipeServer(
    LidGuardRuntimeCoordinator runtimeCoordinator,
    Action requestRuntimeStop)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipeServerStream = new NamedPipeServerStream(
                LidGuardPipeNames.RuntimePipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipeServerStream.WaitForConnectionAsync(cancellationToken);
            _ = HandleConnectionAndDisposeAsync(pipeServerStream, cancellationToken);
        }
    }

    private async Task HandleConnectionAndDisposeAsync(NamedPipeServerStream pipeServerStream, CancellationToken cancellationToken)
    {
        using (pipeServerStream)
        {
            try { await HandleConnectionAsync(pipeServerStream, cancellationToken); }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception) { }
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        var requestJson = await streamReader.ReadLineAsync(cancellationToken);
        if (!TryCreateRequest(requestJson, out var request, out var requestFailureResponse))
        {
            await WriteResponseAsync(streamWriter, requestFailureResponse, cancellationToken);
            return;
        }

        if (request.Command == LidGuardPipeCommands.LiveStatus)
        {
            await StreamLiveStatusSnapshotsAsync(streamReader, streamWriter, cancellationToken);
            return;
        }

        var response = await runtimeCoordinator.HandleAsync(request, cancellationToken);
        try
        {
            await WriteResponseAsync(streamWriter, response, cancellationToken);
        }
        finally
        {
            if (await runtimeCoordinator.TryConsumeServerRuntimeStopRequestAsync(cancellationToken)) requestRuntimeStop();
        }
    }

    private async Task StreamLiveStatusSnapshotsAsync(StreamReader streamReader, StreamWriter streamWriter, CancellationToken cancellationToken)
    {
        using var subscription = runtimeCoordinator.LiveStatusEvents.Subscribe();
        var clientDisconnectTask = streamReader.ReadLineAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await runtimeCoordinator.CreateLiveStatusSnapshotAsync(cancellationToken);
            var snapshotJson = JsonSerializer.Serialize(snapshot, LidGuardJsonSerializerContext.Default.LiveStatusSnapshot);
            await streamWriter.WriteLineAsync(snapshotJson.AsMemory(), cancellationToken);
            var changeTask = subscription.WaitForChangeAsync(cancellationToken);
            var completedTask = await Task.WhenAny(changeTask, clientDisconnectTask);
            if (ReferenceEquals(completedTask, clientDisconnectTask))
            {
                try { await clientDisconnectTask; }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                break;
            }

            await changeTask;
        }
    }

    private static async Task WriteResponseAsync(StreamWriter streamWriter, LidGuardPipeResponse response, CancellationToken cancellationToken)
    {
        var responseJson = JsonSerializer.Serialize(response, LidGuardJsonSerializerContext.Default.LidGuardPipeResponse);
        await streamWriter.WriteLineAsync(responseJson.AsMemory(), cancellationToken);
    }

    private static bool TryCreateRequest(string requestJson, out LidGuardPipeRequest request, out LidGuardPipeResponse response)
    {
        request = null;
        response = null;
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            response = LidGuardPipeResponse.Failure("The runtime received an empty request.");
            return false;
        }

        try
        {
            request = JsonSerializer.Deserialize(requestJson, LidGuardJsonSerializerContext.Default.LidGuardPipeRequest);
            if (request is not null) return true;

            response = LidGuardPipeResponse.Failure("The runtime could not parse the request.");
            return false;
        }
        catch (JsonException exception)
        {
            response = LidGuardPipeResponse.Failure($"The runtime received invalid JSON: {exception.Message}");
            return false;
        }
    }
}

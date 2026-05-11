using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LidGuard.Localization;

namespace LidGuard.Ipc;

internal sealed class LidGuardRuntimeClient
{
    private static readonly TimeSpan s_runtimeAutoStartProbeTimeout = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan s_runtimeConnectionAttemptTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_runtimeConnectionTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan s_runtimeStartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly string s_unixDetachedLauncherScript =
        "if command -v setsid >/dev/null 2>&1; then setsid \"$@\" </dev/null >/dev/null 2>&1 & " +
        "elif command -v nohup >/dev/null 2>&1; then nohup \"$@\" </dev/null >/dev/null 2>&1 & " +
        "else \"$@\" </dev/null >/dev/null 2>&1 & fi";

    public async Task<LidGuardPipeResponse> SendAsync(
        LidGuardPipeRequest request,
        bool startRuntimeIfUnavailable,
        CancellationToken cancellationToken = default)
    {
        var pipeClientStream = startRuntimeIfUnavailable
            ? await TryConnectAsync(s_runtimeAutoStartProbeTimeout, cancellationToken)
            : await WaitForRuntimeAsync(s_runtimeConnectionTimeout, cancellationToken);
        if (pipeClientStream is null && startRuntimeIfUnavailable)
        {
            if (!TryStartRuntime())
            {
                return LidGuardPipeResponse.Failure(
                    "Failed to start the LidGuard runtime.",
                    runtimeUnavailable: true,
                    messageCode: LidGuardPipeResponseMessageCodes.FailedToStartRuntime);
            }

            pipeClientStream = await WaitForRuntimeAsync(s_runtimeStartupTimeout, cancellationToken);
        }

        if (pipeClientStream is null)
        {
            return LidGuardPipeResponse.Failure(
                "LidGuard runtime is not running.",
                runtimeUnavailable: true,
                messageCode: LidGuardPipeResponseMessageCodes.RuntimeNotRunning);
        }

        using (pipeClientStream)
        {
            return await SendConnectedAsync(pipeClientStream, request, cancellationToken);
        }
    }

    public async IAsyncEnumerable<LiveStatusSnapshot> SubscribeLiveStatusAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pipeClientStream = await WaitForRuntimeAsync(s_runtimeConnectionTimeout, cancellationToken);
        if (pipeClientStream is null)
        {
            yield return LiveStatusSnapshot.RuntimeUnavailable();
            yield break;
        }

        var streamReader = new StreamReader(pipeClientStream, Encoding.UTF8, false, 4096, true);
        var streamWriter = new StreamWriter(pipeClientStream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        try
        {
            var request = new LidGuardPipeRequest { Command = LidGuardPipeCommands.LiveStatus };
            var requestJson = JsonSerializer.Serialize(request, LidGuardJsonSerializerContext.Default.LidGuardPipeRequest);
            var connectionFailureSnapshot = await TryWriteLiveStatusRequestAsync(streamWriter, requestJson, cancellationToken);
            if (connectionFailureSnapshot is not null)
            {
                yield return connectionFailureSnapshot;
                yield break;
            }

            var receivedSnapshot = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                var snapshotJson = string.Empty;
                connectionFailureSnapshot = null;
                var readWasCanceled = false;
                try { snapshotJson = await streamReader.ReadLineAsync(cancellationToken); }
                catch (IOException exception) { connectionFailureSnapshot = CreateLiveStatusConnectionFailureSnapshot(exception); }
                catch (ObjectDisposedException exception) { connectionFailureSnapshot = CreateLiveStatusConnectionFailureSnapshot(exception); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { readWasCanceled = true; }
                if (readWasCanceled) yield break;
                if (connectionFailureSnapshot is not null)
                {
                    if (!receivedSnapshot) yield return connectionFailureSnapshot;
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(snapshotJson)) break;

                LiveStatusSnapshot snapshot;
                var shouldStop = false;
                try
                {
                    snapshot = JsonSerializer.Deserialize(snapshotJson, LidGuardJsonSerializerContext.Default.LiveStatusSnapshot)
                        ?? LiveStatusSnapshot.Failure("The LidGuard runtime live-status snapshot could not be parsed.");
                }
                catch (JsonException exception)
                {
                    snapshot = LiveStatusSnapshot.Failure($"The LidGuard runtime returned an invalid live-status snapshot: {exception.Message}");
                    shouldStop = true;
                }

                receivedSnapshot = true;
                yield return snapshot;
                if (shouldStop) yield break;
            }

            if (!receivedSnapshot) yield return LiveStatusSnapshot.Failure("The LidGuard runtime live-status stream ended before sending a snapshot.");
        }
        finally
        {
            DisposeLiveStatusResource(streamWriter);
            DisposeLiveStatusResource(streamReader);
            DisposeLiveStatusResource(pipeClientStream);
        }
    }

    private static async Task<LiveStatusSnapshot> TryWriteLiveStatusRequestAsync(
        StreamWriter streamWriter,
        string requestJson,
        CancellationToken cancellationToken)
    {
        try { await streamWriter.WriteLineAsync(requestJson.AsMemory(), cancellationToken); return null; }
        catch (IOException exception) { return CreateLiveStatusConnectionFailureSnapshot(exception); }
        catch (ObjectDisposedException exception) { return CreateLiveStatusConnectionFailureSnapshot(exception); }
    }

    private static LiveStatusSnapshot CreateLiveStatusConnectionFailureSnapshot(Exception exception)
        => LiveStatusSnapshot.RuntimeUnavailable($"Failed to connect to the LidGuard runtime live-status stream: {exception.Message}");

    private static void DisposeLiveStatusResource(IDisposable resource)
    {
        try { resource.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (NotSupportedException) { }
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
            var pipeClientStream = await TryConnectAsync(s_runtimeConnectionAttemptTimeout, cancellationToken);
            if (pipeClientStream is not null) return pipeClientStream;
            await Task.Delay(100, cancellationToken);
        }

        return null;
    }

    private static async Task<NamedPipeClientStream> TryConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pipeClientStream = new NamedPipeClientStream(
            ".",
            LidGuardPipeNames.RuntimePipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            var timeoutMilliseconds = Math.Max(1, (int)timeout.TotalMilliseconds);
            await pipeClientStream.ConnectAsync(timeoutMilliseconds, cancellationToken);
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
        if (!TryCreateRuntimeProcessStartInfo(out var processStartInfo)) return false;

        try
        {
            if (!OperatingSystem.IsWindows()) return TryStartUnixDetachedRuntime(processStartInfo);

            using var process = Process.Start(processStartInfo);
            return process is not null;
        }
        catch { return false; }
    }

    private static bool TryCreateRuntimeProcessStartInfo(out ProcessStartInfo processStartInfo)
    {
        processStartInfo = null;
        var runtimeExecutablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(runtimeExecutablePath)) return false;

        processStartInfo = new ProcessStartInfo
        {
            FileName = runtimeExecutablePath,
            WorkingDirectory = AppContext.BaseDirectory
        };

        if (!TryAddRuntimeArguments(processStartInfo, runtimeExecutablePath)) return false;
        if (OperatingSystem.IsWindows())
        {
            processStartInfo.UseShellExecute = true;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }
        else
        {
            processStartInfo.UseShellExecute = false;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.CreateNoWindow = true;
        }

        LidGuardCulture.ConfigureChildProcessCulture(processStartInfo);
        return true;
    }

    private static bool TryAddRuntimeArguments(ProcessStartInfo processStartInfo, string runtimeExecutablePath)
    {
        if (IsDotnetHost(runtimeExecutablePath))
        {
            var runtimeAssemblyPath = Path.Combine(AppContext.BaseDirectory, "lidguard.dll");
            if (!File.Exists(runtimeAssemblyPath)) return false;

            processStartInfo.ArgumentList.Add(runtimeAssemblyPath);
        }

        processStartInfo.ArgumentList.Add(LidGuardPipeCommands.RunServer);
        return true;
    }

    private static bool IsDotnetHost(string runtimeExecutablePath)
        => Path.GetFileNameWithoutExtension(runtimeExecutablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    private static bool TryStartUnixDetachedRuntime(ProcessStartInfo runtimeProcessStartInfo)
    {
        if (!File.Exists("/bin/sh")) return TryStartProcess(runtimeProcessStartInfo);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        processStartInfo.ArgumentList.Add("-c");
        processStartInfo.ArgumentList.Add(s_unixDetachedLauncherScript);
        processStartInfo.ArgumentList.Add("lidguard-runtime-launcher");
        processStartInfo.ArgumentList.Add(runtimeProcessStartInfo.FileName);
        foreach (var argument in runtimeProcessStartInfo.ArgumentList) processStartInfo.ArgumentList.Add(argument);

        LidGuardCulture.ConfigureChildProcessCulture(processStartInfo);
        using var process = Process.Start(processStartInfo);
        if (process is null) return false;
        if (!process.WaitForExit((int)TimeSpan.FromSeconds(2).TotalMilliseconds)) return true;
        return process.ExitCode == 0;
    }

    private static bool TryStartProcess(ProcessStartInfo processStartInfo)
    {
        using var process = Process.Start(processStartInfo);
        return process is not null;
    }
}

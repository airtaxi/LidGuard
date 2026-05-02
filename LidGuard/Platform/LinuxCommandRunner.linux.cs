using System.Diagnostics;

namespace LidGuard.Platform;

internal static class LinuxCommandRunner
{
    public static LinuxCommandResult Run(string fileName, IEnumerable<string> arguments, TimeSpan timeout = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            try { return RunAsync(fileName, arguments, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { return LinuxCommandResult.Failure($"Command was canceled: {fileName}"); }
        }

        using var timeoutCancellationTokenSource = new CancellationTokenSource(timeout);
        try { return RunAsync(fileName, arguments, timeoutCancellationTokenSource.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return LinuxCommandResult.Failure($"Command timed out after {timeout.TotalSeconds:0} second(s): {fileName}"); }
    }

    public static async Task<LinuxCommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var processStartInformation = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments ?? Array.Empty<string>()) processStartInformation.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(processStartInformation);
            if (process is null) return LinuxCommandResult.Failure($"Failed to start command: {fileName}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return LinuxCommandResult.Failure($"Failed to start command {fileName}: {exception.Message}");
        }

        using (process)
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            return LinuxCommandResult.Success(process.ExitCode, standardOutput, standardError);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}

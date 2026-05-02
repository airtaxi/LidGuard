using System.Diagnostics;
using LidGuard.Platform;
using LidGuard.Results;

namespace LidGuard.Power;

internal sealed class CaffeinateAssertion : IDisposable
{
    private static readonly TimeSpan s_startupProbeDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_disposeWaitTimeout = TimeSpan.FromSeconds(2);
    private readonly Process _process;
    private bool _disposed;

    private CaffeinateAssertion(Process process)
    {
        _process = process;
    }

    public bool IsActive
    {
        get
        {
            try { return !_process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public static LidGuardOperationResult<CaffeinateAssertion> TryAcquire(IEnumerable<string> assertionFlags)
    {
        if (!MacOSCommandPathResolver.TryFindExecutable("caffeinate", out var caffeinatePath))
            return LidGuardOperationResult<CaffeinateAssertion>.Failure("caffeinate was not found on PATH. LidGuard macOS support requires /usr/bin/caffeinate.");

        var processStartInformation = new ProcessStartInfo
        {
            FileName = caffeinatePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var assertionFlag in assertionFlags) processStartInformation.ArgumentList.Add(assertionFlag);
        processStartInformation.ArgumentList.Add("/bin/sh");
        processStartInformation.ArgumentList.Add("-c");
        processStartInformation.ArgumentList.Add("read _");

        Process process;
        try
        {
            process = Process.Start(processStartInformation);
            if (process is null) return LidGuardOperationResult<CaffeinateAssertion>.Failure("Failed to start caffeinate.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return LidGuardOperationResult<CaffeinateAssertion>.Failure($"Failed to start caffeinate: {exception.Message}");
        }

        Thread.Sleep(s_startupProbeDelay);
        if (!HasExited(process)) return LidGuardOperationResult<CaffeinateAssertion>.Success(new CaffeinateAssertion(process));

        var exitCode = GetExitCode(process);
        var standardError = ReadToEnd(process.StandardError);
        var standardOutput = ReadToEnd(process.StandardOutput);
        process.Dispose();

        var detail = !string.IsNullOrWhiteSpace(standardError) ? standardError.Trim() : standardOutput.Trim();
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"caffeinate exited before acquiring the assertion. Exit code: {exitCode}."
            : $"caffeinate exited before acquiring the assertion. Exit code: {exitCode}. {detail}";
        return LidGuardOperationResult<CaffeinateAssertion>.Failure(message, exitCode);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try { _process.StandardInput.Dispose(); }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException) { }

        try
        {
            if (!_process.WaitForExit((int)s_disposeWaitTimeout.TotalMilliseconds)) _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }

        _process.Dispose();
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static int GetExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private static string ReadToEnd(StreamReader streamReader)
    {
        try { return streamReader.ReadToEnd(); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { return string.Empty; }
    }
}

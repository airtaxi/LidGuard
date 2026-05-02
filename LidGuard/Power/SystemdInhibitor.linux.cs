using System.Diagnostics;
using LidGuard.Platform;
using LidGuard.Results;

namespace LidGuard.Power;

internal sealed class SystemdInhibitor : IDisposable
{
    private static readonly TimeSpan s_startupProbeDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_disposeWaitTimeout = TimeSpan.FromSeconds(2);
    private readonly Process _process;
    private bool _disposed;

    private SystemdInhibitor(Process process)
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

    public static LidGuardOperationResult<SystemdInhibitor> TryAcquire(string inhibitorTypesText, string reason)
    {
        if (string.IsNullOrWhiteSpace(inhibitorTypesText)) return LidGuardOperationResult<SystemdInhibitor>.Failure("A systemd inhibitor type is required.");
        if (!LinuxCommandPathResolver.TryFindExecutable("systemd-inhibit", out var systemdInhibitPath))
            return LidGuardOperationResult<SystemdInhibitor>.Failure("systemd-inhibit was not found on PATH. LidGuard Linux support requires systemd/logind.");

        var processStartInformation = new ProcessStartInfo
        {
            FileName = systemdInhibitPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        processStartInformation.ArgumentList.Add($"--what={inhibitorTypesText}");
        processStartInformation.ArgumentList.Add("--mode=block");
        processStartInformation.ArgumentList.Add("--who=LidGuard");
        processStartInformation.ArgumentList.Add($"--why={NormalizeReason(reason)}");
        processStartInformation.ArgumentList.Add("--");
        processStartInformation.ArgumentList.Add("sh");
        processStartInformation.ArgumentList.Add("-c");
        processStartInformation.ArgumentList.Add("read _");

        Process process;
        try
        {
            process = Process.Start(processStartInformation);
            if (process is null) return LidGuardOperationResult<SystemdInhibitor>.Failure("Failed to start systemd-inhibit.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return LidGuardOperationResult<SystemdInhibitor>.Failure($"Failed to start systemd-inhibit: {exception.Message}");
        }

        Thread.Sleep(s_startupProbeDelay);
        if (!HasExited(process)) return LidGuardOperationResult<SystemdInhibitor>.Success(new SystemdInhibitor(process));

        var exitCode = GetExitCode(process);
        var standardError = ReadToEnd(process.StandardError);
        var standardOutput = ReadToEnd(process.StandardOutput);
        process.Dispose();

        var detail = !string.IsNullOrWhiteSpace(standardError) ? standardError.Trim() : standardOutput.Trim();
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"systemd-inhibit exited before acquiring the inhibitor. Exit code: {exitCode}."
            : $"systemd-inhibit exited before acquiring the inhibitor. Exit code: {exitCode}. {detail}";
        return LidGuardOperationResult<SystemdInhibitor>.Failure(message, exitCode);
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

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "LidGuard is keeping the system awake while an agent session is running.";
        return reason.Trim();
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

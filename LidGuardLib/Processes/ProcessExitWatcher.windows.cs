using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace LidGuardLib.Processes;

[SupportedOSPlatform("windows6.1")]
public sealed class ProcessExitWatcher : IProcessExitWatcher
{
    public async Task<LidGuardOperationResult> WaitForExitAsync(int processIdentifier, TimeSpan checkInterval, CancellationToken cancellationToken = default)
    {
        if (processIdentifier <= 0) return LidGuardOperationResult.Failure("A process identifier is required.");

        var accessRights = PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE | PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION;
        using var processHandle = PInvoke.OpenProcess_SafeHandle(accessRights, false, (uint)processIdentifier);
        if (processHandle.IsInvalid) return LidGuardOperationResult.Failure("Failed to open the process to watch.", Marshal.GetLastPInvokeError());

        var waitMilliseconds = GetWaitMilliseconds(checkInterval);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var waitResult = PInvoke.WaitForSingleObject(processHandle, waitMilliseconds);
            if (waitResult == WAIT_EVENT.WAIT_OBJECT_0) return LidGuardOperationResult.Success();
            if (waitResult == WAIT_EVENT.WAIT_TIMEOUT)
            {
                await Task.Yield();
                continue;
            }

            return LidGuardOperationResult.Failure("Failed while waiting for the process to exit.", Marshal.GetLastPInvokeError());
        }
    }

    private static uint GetWaitMilliseconds(TimeSpan checkInterval)
    {
        if (checkInterval <= TimeSpan.Zero) return 1000;
        if (checkInterval.TotalMilliseconds >= int.MaxValue) return int.MaxValue;
        return (uint)Math.Max(1, (int)checkInterval.TotalMilliseconds);
    }
}

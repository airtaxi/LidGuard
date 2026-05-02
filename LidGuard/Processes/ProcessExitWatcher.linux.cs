using System.Diagnostics;
using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Processes;

public sealed class ProcessExitWatcher : IProcessExitWatcher
{
    public async Task<LidGuardOperationResult> WaitForExitAsync(int processIdentifier, TimeSpan _, CancellationToken cancellationToken = default)
    {
        if (processIdentifier <= 0) return LidGuardOperationResult.Failure("A process identifier is required.");

        Process process;
        try { process = Process.GetProcessById(processIdentifier); }
        catch (ArgumentException) { return LidGuardOperationResult.Success(); }
        catch (InvalidOperationException) { return LidGuardOperationResult.Success(); }

        using (process)
        {
            try
            {
                if (process.HasExited) return LidGuardOperationResult.Success();
                await process.WaitForExitAsync(cancellationToken);
                return LidGuardOperationResult.Success();
            }
            catch (InvalidOperationException) { return LidGuardOperationResult.Success(); }
        }
    }
}

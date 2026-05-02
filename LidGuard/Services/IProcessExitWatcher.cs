using LidGuard.Results;

namespace LidGuard.Services;

public interface IProcessExitWatcher
{
    Task<LidGuardOperationResult> WaitForExitAsync(int processIdentifier, TimeSpan checkInterval, CancellationToken cancellationToken = default);
}

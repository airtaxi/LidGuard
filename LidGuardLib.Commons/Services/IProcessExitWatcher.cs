using LidGuardLib.Commons.Results;

namespace LidGuardLib.Commons.Services;

public interface IProcessExitWatcher
{
    Task<LidGuardOperationResult> WaitForExitAsync(int processIdentifier, TimeSpan checkInterval, CancellationToken cancellationToken = default);
}

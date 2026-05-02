using LidGuard.Results;

namespace LidGuard.Services;

public interface IPostStopSuspendSoundPlayer
{
    LidGuardOperationResult<string> NormalizeConfiguration(string configuredValue);

    Task<LidGuardOperationResult> PlayAsync(string configuredValue, CancellationToken cancellationToken);
}

using LidGuardLib.Commons.Results;

namespace LidGuardLib.Commons.Services;

public interface IPostStopSuspendSoundPlayer
{
    LidGuardOperationResult<string> NormalizeConfiguration(string configuredValue);

    Task<LidGuardOperationResult> PlayAsync(string configuredValue, CancellationToken cancellationToken);
}

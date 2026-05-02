using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class PowerRequestService : IPowerRequestService
{
    public LidGuardOperationResult<ILidGuardPowerRequest> Create(PowerRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.HasAnyRequest) return LidGuardOperationResult<ILidGuardPowerRequest>.Success(InactivePowerRequest.Instance);

        var assertionFlags = CreateAssertionFlags(options);
        if (assertionFlags.Count == 0) return LidGuardOperationResult<ILidGuardPowerRequest>.Success(InactivePowerRequest.Instance);

        var assertionResult = CaffeinateAssertion.TryAcquire(assertionFlags);
        if (!assertionResult.Succeeded) return LidGuardOperationResult<ILidGuardPowerRequest>.Failure(assertionResult.Message, assertionResult.NativeErrorCode);

        return LidGuardOperationResult<ILidGuardPowerRequest>.Success(new PowerRequest(assertionResult.Value));
    }

    private static IReadOnlyList<string> CreateAssertionFlags(PowerRequestOptions options)
    {
        var assertionFlags = new List<string>();
        if (options.PreventSystemSleep) assertionFlags.Add("-i");
        if (options.PreventDisplaySleep) assertionFlags.Add("-d");
        return assertionFlags;
    }

    private sealed class InactivePowerRequest : ILidGuardPowerRequest
    {
        public static InactivePowerRequest Instance { get; } = new();

        public bool IsActive => false;

        public void Dispose()
        {
        }
    }
}

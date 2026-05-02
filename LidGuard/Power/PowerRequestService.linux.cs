using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class PowerRequestService : IPowerRequestService
{
    public LidGuardOperationResult<ILidGuardPowerRequest> Create(PowerRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.HasAnyRequest) return LidGuardOperationResult<ILidGuardPowerRequest>.Success(InactivePowerRequest.Instance);

        var inhibitorTypes = CreateInhibitorTypes(options);
        if (inhibitorTypes.Count == 0) return LidGuardOperationResult<ILidGuardPowerRequest>.Success(InactivePowerRequest.Instance);

        var reason = string.IsNullOrWhiteSpace(options.Reason) ? PowerRequestOptions.Default.Reason : options.Reason;
        var inhibitorResult = SystemdInhibitor.TryAcquire(string.Join(':', inhibitorTypes), reason);
        if (!inhibitorResult.Succeeded) return LidGuardOperationResult<ILidGuardPowerRequest>.Failure(inhibitorResult.Message, inhibitorResult.NativeErrorCode);

        return LidGuardOperationResult<ILidGuardPowerRequest>.Success(new PowerRequest(inhibitorResult.Value));
    }

    private static IReadOnlyList<string> CreateInhibitorTypes(PowerRequestOptions options)
    {
        var inhibitorTypes = new List<string>();
        if (options.PreventSystemSleep) inhibitorTypes.Add("sleep");
        if (options.PreventSystemSleep || options.PreventDisplaySleep) inhibitorTypes.Add("idle");
        return inhibitorTypes;
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

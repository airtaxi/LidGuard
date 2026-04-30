using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace LidGuardLib.Power;

[SupportedOSPlatform("windows6.1")]
public sealed class PowerRequestService : IPowerRequestService
{
    public unsafe LidGuardOperationResult<ILidGuardPowerRequest> Create(PowerRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.HasAnyRequest) return LidGuardOperationResult<ILidGuardPowerRequest>.Success(InactivePowerRequest.Instance);

        var reason = string.IsNullOrWhiteSpace(options.Reason) ? PowerRequestOptions.Default.Reason : options.Reason;

        fixed (char* reasonPointer = reason)
        {
            var context = new REASON_CONTEXT
            {
                Version = 0,
                Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                Reason = new REASON_CONTEXT._Reason_e__Union
                {
                    SimpleReasonString = new PWSTR(reasonPointer)
                }
            };

            var handle = PInvoke.PowerCreateRequest(context);
            if (handle.IsInvalid)
            {
                var nativeErrorCode = Marshal.GetLastPInvokeError();
                handle.Dispose();
                return LidGuardOperationResult<ILidGuardPowerRequest>.Failure("Failed to create a Windows power request.", nativeErrorCode);
            }

            var powerRequest = new PowerRequest(handle, options.PreventSystemSleep, options.PreventAwayModeSleep, options.PreventDisplaySleep);

            if (options.PreventSystemSleep && !PInvoke.PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
            {
                var nativeErrorCode = Marshal.GetLastPInvokeError();
                powerRequest.Dispose();
                return LidGuardOperationResult<ILidGuardPowerRequest>.Failure("Failed to set the system-required power request.", nativeErrorCode);
            }

            if (options.PreventAwayModeSleep && !PInvoke.PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestAwayModeRequired))
            {
                var nativeErrorCode = Marshal.GetLastPInvokeError();
                powerRequest.Dispose();
                return LidGuardOperationResult<ILidGuardPowerRequest>.Failure("Failed to set the away-mode-required power request.", nativeErrorCode);
            }

            if (options.PreventDisplaySleep && !PInvoke.PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestDisplayRequired))
            {
                var nativeErrorCode = Marshal.GetLastPInvokeError();
                powerRequest.Dispose();
                return LidGuardOperationResult<ILidGuardPowerRequest>.Failure("Failed to set the display-required power request.", nativeErrorCode);
            }

            return LidGuardOperationResult<ILidGuardPowerRequest>.Success(powerRequest);
        }
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

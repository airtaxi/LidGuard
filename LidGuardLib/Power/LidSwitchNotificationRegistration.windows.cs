using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LidGuardLib.Power;

[SupportedOSPlatform("windows6.1")]
public sealed class LidSwitchNotificationRegistration : IDisposable
{
    public static readonly Guid LidSwitchStateChangeIdentifier = new("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3");

    private HPOWERNOTIFY _notificationHandle;

    private LidSwitchNotificationRegistration(HPOWERNOTIFY notificationHandle)
    {
        _notificationHandle = notificationHandle;
    }

    public static unsafe LidGuardOperationResult<LidSwitchNotificationRegistration> RegisterWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return LidGuardOperationResult<LidSwitchNotificationRegistration>.Failure("A window handle is required.");

        var powerSettingIdentifier = LidSwitchStateChangeIdentifier;
        var notificationHandle = PInvoke.RegisterPowerSettingNotification((HANDLE)windowHandle, &powerSettingIdentifier, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (notificationHandle.IsNull) return LidGuardOperationResult<LidSwitchNotificationRegistration>.Failure("Failed to register the lid switch power notification.", Marshal.GetLastPInvokeError());

        return LidGuardOperationResult<LidSwitchNotificationRegistration>.Success(new LidSwitchNotificationRegistration(notificationHandle));
    }

    public static LidSwitchState FromPowerBroadcastValue(uint value)
    {
        return value switch
        {
            0 => LidSwitchState.Closed,
            1 => LidSwitchState.Open,
            _ => LidSwitchState.Unknown
        };
    }

    public void Dispose()
    {
        if (_notificationHandle.IsNull) return;

        PInvoke.UnregisterPowerSettingNotification(_notificationHandle);
        _notificationHandle = HPOWERNOTIFY.Null;
    }
}

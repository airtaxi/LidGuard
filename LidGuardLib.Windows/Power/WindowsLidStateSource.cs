using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LidGuardLib.Windows.Power;

[SupportedOSPlatform("windows6.1")]
internal sealed unsafe partial class WindowsLidStateSource : ILidStateSource, IDisposable
{
    private const uint PowerBroadcastSettingChange = 0x8013;
    private static readonly delegate* unmanaged<IntPtr, uint, IntPtr, uint> s_powerSettingCallback = &HandlePowerSettingChanged;

    private GCHandle _contextHandle;
    private HPOWERNOTIFY _registrationHandle;
    private int _currentStateValue = (int)LidSwitchState.Unknown;

    public LidSwitchState CurrentState => (LidSwitchState)Volatile.Read(ref _currentStateValue);

    public WindowsLidStateSource()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2)) return;

        _contextHandle = GCHandle.Alloc(this);

        var subscribeParameters = new DeviceNotifySubscribeParameters
        {
            Callback = s_powerSettingCallback,
            Context = GCHandle.ToIntPtr(_contextHandle)
        };

        var lidSwitchStateChangeIdentifier = WindowsLidSwitchNotificationRegistration.LidSwitchStateChangeIdentifier;
        var lidSwitchStateChangeIdentifierPointer = &lidSwitchStateChangeIdentifier;
        {
            void* registrationHandleValue = null;
            var registrationResult = PInvoke.PowerSettingRegisterNotification(
                lidSwitchStateChangeIdentifierPointer,
                REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_CALLBACK,
                (HANDLE)(IntPtr)(&subscribeParameters),
                &registrationHandleValue);

            if (Succeeded(registrationResult))
            {
                _registrationHandle = (HPOWERNOTIFY)(IntPtr)registrationHandleValue;
                return;
            }
        }

        _registrationHandle = default;
        _contextHandle.Free();
    }

    public void Dispose()
    {
        if (_registrationHandle != default)
        {
            PInvoke.PowerSettingUnregisterNotification(_registrationHandle);
            _registrationHandle = default;
        }

        if (_contextHandle.IsAllocated) _contextHandle.Free();
    }

    [UnmanagedCallersOnly]
    private static uint HandlePowerSettingChanged(IntPtr context, uint type, IntPtr setting)
    {
        if (type != PowerBroadcastSettingChange) return 0;
        if (context == IntPtr.Zero || setting == IntPtr.Zero) return 0;

        try
        {
            var contextHandle = GCHandle.FromIntPtr(context);
            if (contextHandle.Target is not WindowsLidStateSource lidStateSource) return 0;

            var powerBroadcastSetting = (PowerBroadcastSetting*)setting;
            if (powerBroadcastSetting->DataLength < sizeof(uint)) return 0;
            if (powerBroadcastSetting->PowerSetting != WindowsLidSwitchNotificationRegistration.LidSwitchStateChangeIdentifier) return 0;

            var lidSwitchState = WindowsLidSwitchNotificationRegistration.FromPowerBroadcastValue(powerBroadcastSetting->Data);
            Volatile.Write(ref lidStateSource._currentStateValue, (int)lidSwitchState);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidCastException or ArgumentException) { }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerBroadcastSetting
    {
        public Guid PowerSetting;

        public uint DataLength;

        public uint Data;
    }

    private static bool Succeeded(WIN32_ERROR nativeError) => (uint)nativeError == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public delegate* unmanaged<IntPtr, uint, IntPtr, uint> Callback;

        public IntPtr Context;
    }
}

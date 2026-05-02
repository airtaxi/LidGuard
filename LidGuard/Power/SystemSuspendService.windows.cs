using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuard.Power;
using LidGuard.Results;
using LidGuard.Services;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Power;

namespace LidGuard.Power;

[SupportedOSPlatform("windows6.1")]
public sealed class SystemSuspendService : ISystemSuspendService
{
    private const int ErrorNotAllAssigned = 1300;
    private const int ErrorNotSupported = 50;
    private const string ShutdownPrivilegeName = "SeShutdownPrivilege";
    private const uint WindowMessageSystemCommand = 0x0112;
    private const nuint SystemCommandMonitorPower = 0xF170;
    private const nint MonitorPowerOff = 2;
    private static readonly HWND s_broadcastWindowHandle = new(new IntPtr(0xffff));

    public LidGuardOperationResult Suspend(SystemSuspendMode suspendMode)
    {
        var privilegeResult = EnableShutdownPrivilege();
        if (!privilegeResult.Succeeded) return privilegeResult;

        if (suspendMode == SystemSuspendMode.Hibernate) return TryHibernate();
        return TrySleepOrModernStandby();
    }

    private static LidGuardOperationResult TryHibernate()
    {
        if (!PInvoke.SetSuspendState(true, false, false)) return LidGuardOperationResult.Failure("Failed to suspend the system.", Marshal.GetLastPInvokeError());
        return LidGuardOperationResult.Success();
    }

    private static LidGuardOperationResult TrySleepOrModernStandby()
    {
        if (PInvoke.SetSuspendState(false, false, false)) return LidGuardOperationResult.Success();

        var nativeErrorCode = Marshal.GetLastPInvokeError();
        if (nativeErrorCode != ErrorNotSupported) return LidGuardOperationResult.Failure("Failed to suspend the system.", nativeErrorCode);

        var powerCapabilitiesResult = TryGetSystemPowerCapabilities(out var systemPowerCapabilities);
        if (!powerCapabilitiesResult.Succeeded) return powerCapabilitiesResult;
        if (systemPowerCapabilities.AoAc == 0) return LidGuardOperationResult.Failure("Failed to suspend the system.", nativeErrorCode);

        return TryRequestModernStandbyByTurningOffDisplay();
    }

    private static LidGuardOperationResult TryGetSystemPowerCapabilities(out SYSTEM_POWER_CAPABILITIES systemPowerCapabilities)
    {
        systemPowerCapabilities = default;
        if (PInvoke.GetPwrCapabilities(out systemPowerCapabilities)) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure("Failed to query system power capabilities.", Marshal.GetLastPInvokeError());
    }

    private static LidGuardOperationResult TryRequestModernStandbyByTurningOffDisplay()
    {
        if (PInvoke.SendNotifyMessage(s_broadcastWindowHandle, WindowMessageSystemCommand, new WPARAM(SystemCommandMonitorPower), new LPARAM(MonitorPowerOff)))
            return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure("Failed to request Modern Standby by turning off the display.", Marshal.GetLastPInvokeError());
    }

    private static unsafe LidGuardOperationResult EnableShutdownPrivilege()
    {
        using var currentProcessHandle = new SafeFileHandle((IntPtr)PInvoke.GetCurrentProcess(), ownsHandle: false);

        if (!PInvoke.OpenProcessToken(currentProcessHandle, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, out var tokenHandle))
        {
            tokenHandle.Dispose();
            return LidGuardOperationResult.Failure("Failed to open the current process token.", Marshal.GetLastPInvokeError());
        }

        using (tokenHandle)
        {
            if (!PInvoke.LookupPrivilegeValue(null, ShutdownPrivilegeName, out var privilegeIdentifier)) return LidGuardOperationResult.Failure("Failed to look up the shutdown privilege.", Marshal.GetLastPInvokeError());

            var tokenPrivileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1
            };

            tokenPrivileges.Privileges[0] = new LUID_AND_ATTRIBUTES
            {
                Luid = privilegeIdentifier,
                Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED
            };

            if (!PInvoke.AdjustTokenPrivileges(tokenHandle, false, &tokenPrivileges, [])) return LidGuardOperationResult.Failure("Failed to enable the shutdown privilege.", Marshal.GetLastPInvokeError());

            var nativeErrorCode = Marshal.GetLastPInvokeError();
            if (nativeErrorCode == ErrorNotAllAssigned) return LidGuardOperationResult.Failure("The shutdown privilege is not assigned to this process token.", nativeErrorCode);
        }

        return LidGuardOperationResult.Success();
    }
}

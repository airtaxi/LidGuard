using System.Runtime.Versioning;
using LidGuardLib.Commons.Services;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace LidGuardLib.Windows.Power;

[SupportedOSPlatform("windows6.1")]
internal sealed class WindowsPowerRequest(
    SafeFileHandle handle,
    bool hasSystemRequest,
    bool hasAwayModeRequest,
    bool hasDisplayRequest) : ILidGuardPowerRequest
{
    public bool IsActive { get; private set; } = true;

    public void Dispose()
    {
        if (!IsActive) return;

        if (hasSystemRequest) PInvoke.PowerClearRequest(handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
        if (hasAwayModeRequest) PInvoke.PowerClearRequest(handle, POWER_REQUEST_TYPE.PowerRequestAwayModeRequired);
        if (hasDisplayRequest) PInvoke.PowerClearRequest(handle, POWER_REQUEST_TYPE.PowerRequestDisplayRequired);

        handle.Dispose();
        IsActive = false;
    }
}

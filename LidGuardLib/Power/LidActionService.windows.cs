using System.Runtime.Versioning;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace LidGuardLib.Power;

[SupportedOSPlatform("windows6.1")]
public sealed class LidActionService : ILidActionService
{
    private static readonly Guid s_systemButtonSubgroupIdentifier = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid s_lidActionSettingIdentifier = new("5ca83367-6e45-459f-a27b-476b1d01c936");

    public unsafe LidGuardOperationResult<Guid> GetActivePowerSchemeIdentifier()
    {
        var nativeError = PInvoke.PowerGetActiveScheme(null, out var activePowerSchemePointer);
        if (!Succeeded(nativeError)) return LidGuardOperationResult<Guid>.Failure("Failed to read the active Windows power scheme.", (int)(uint)nativeError);

        try
        {
            return LidGuardOperationResult<Guid>.Success(*activePowerSchemePointer);
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)(nint)activePowerSchemePointer);
        }
    }

    public LidGuardOperationResult<LidAction> ReadLidAction(Guid powerSchemeIdentifier, PowerLine powerLine)
    {
        var nativeErrorCode = powerLine == PowerLine.AlternatingCurrent
            ? ReadAlternatingCurrentLidAction(powerSchemeIdentifier, out var value)
            : ReadDirectCurrentLidAction(powerSchemeIdentifier, out value);

        if (nativeErrorCode != 0) return LidGuardOperationResult<LidAction>.Failure("Failed to read the Windows lid close action.", (int)nativeErrorCode);
        return LidGuardOperationResult<LidAction>.Success((LidAction)value);
    }

    public LidGuardOperationResult WriteLidAction(Guid powerSchemeIdentifier, PowerLine powerLine, LidAction lidAction)
    {
        var nativeErrorCode = powerLine == PowerLine.AlternatingCurrent
            ? WriteAlternatingCurrentLidAction(powerSchemeIdentifier, lidAction)
            : WriteDirectCurrentLidAction(powerSchemeIdentifier, lidAction);

        if (nativeErrorCode != 0) return LidGuardOperationResult.Failure("Failed to write the Windows lid close action.", (int)nativeErrorCode);
        return LidGuardOperationResult.Success();
    }

    public LidGuardOperationResult ApplyPowerScheme(Guid powerSchemeIdentifier)
    {
        var nativeError = PInvoke.PowerSetActiveScheme(null, powerSchemeIdentifier);
        if (!Succeeded(nativeError)) return LidGuardOperationResult.Failure("Failed to apply the Windows power scheme.", (int)(uint)nativeError);
        return LidGuardOperationResult.Success();
    }

    private static unsafe uint ReadAlternatingCurrentLidAction(Guid powerSchemeIdentifier, out uint value)
    {
        var nativeError = PInvoke.PowerReadACValueIndex(null, powerSchemeIdentifier, s_systemButtonSubgroupIdentifier, s_lidActionSettingIdentifier, out value);
        return (uint)nativeError;
    }

    private static unsafe uint ReadDirectCurrentLidAction(Guid powerSchemeIdentifier, out uint value)
    {
        return PInvoke.PowerReadDCValueIndex(null, powerSchemeIdentifier, s_systemButtonSubgroupIdentifier, s_lidActionSettingIdentifier, out value);
    }

    private static unsafe uint WriteAlternatingCurrentLidAction(Guid powerSchemeIdentifier, LidAction lidAction)
    {
        var nativeError = PInvoke.PowerWriteACValueIndex(null, powerSchemeIdentifier, s_systemButtonSubgroupIdentifier, s_lidActionSettingIdentifier, (uint)lidAction);
        return (uint)nativeError;
    }

    private static unsafe uint WriteDirectCurrentLidAction(Guid powerSchemeIdentifier, LidAction lidAction)
    {
        return PInvoke.PowerWriteDCValueIndex(null, powerSchemeIdentifier, s_systemButtonSubgroupIdentifier, s_lidActionSettingIdentifier, (uint)lidAction);
    }

    private static bool Succeeded(WIN32_ERROR nativeError) => (uint)nativeError == 0;
}

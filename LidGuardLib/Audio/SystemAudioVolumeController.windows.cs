using System.Runtime.Versioning;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace LidGuardLib.Audio;

[SupportedOSPlatform("windows6.1")]
public sealed partial class SystemAudioVolumeController : ISystemAudioVolumeController
{
    private const int Success = 0;
    private const int SuccessFalse = 1;
    private const int RpcChangedMode = unchecked((int)0x80010106);
    private static readonly Guid s_mmDeviceEnumeratorClassIdentifier = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public LidGuardOperationResult<SystemAudioVolumeState> CaptureDefaultRenderDeviceState()
        => CaptureDefaultRenderDeviceStateCore();

    public LidGuardOperationResult ApplyDefaultRenderDeviceVolumeOverride(int volumeOverridePercent)
    {
        if (!LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(volumeOverridePercent)) return LidGuardOperationResult.Failure($"The volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.");

        return ApplyDefaultRenderDeviceVolumeOverrideCore(volumeOverridePercent);
    }

    public LidGuardOperationResult RestoreDefaultRenderDeviceState(SystemAudioVolumeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return RestoreDefaultRenderDeviceStateCore(state);
    }

    private static unsafe LidGuardOperationResult<SystemAudioVolumeState> CaptureDefaultRenderDeviceStateCore()
    {
        var endpointVolumeResult = CreateDefaultAudioEndpointVolume();
        if (!endpointVolumeResult.Succeeded) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(endpointVolumeResult.Message, endpointVolumeResult.NativeErrorCode);

        using var endpointVolumeHandle = endpointVolumeResult.Value;
        var volumeResult = GetMasterVolumeScalar(endpointVolumeHandle.Pointer, out var masterVolumeScalar);
        if (!volumeResult.Succeeded) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(volumeResult.Message, volumeResult.NativeErrorCode);

        var muteResult = GetMute(endpointVolumeHandle.Pointer, out var isMuted);
        if (!muteResult.Succeeded) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(muteResult.Message, muteResult.NativeErrorCode);

        return LidGuardOperationResult<SystemAudioVolumeState>.Success(new SystemAudioVolumeState
        {
            MasterVolumeScalar = masterVolumeScalar,
            IsMuted = isMuted
        });
    }

    private static unsafe LidGuardOperationResult ApplyDefaultRenderDeviceVolumeOverrideCore(int volumeOverridePercent)
    {
        var endpointVolumeResult = CreateDefaultAudioEndpointVolume();
        if (!endpointVolumeResult.Succeeded) return endpointVolumeResult.ToNonGenericResult();

        using var endpointVolumeHandle = endpointVolumeResult.Value;
        var volumeScalar = volumeOverridePercent / 100.0f;
        var volumeResult = SetMasterVolumeScalar(endpointVolumeHandle.Pointer, volumeScalar);
        var muteResult = SetMute(endpointVolumeHandle.Pointer, false);
        return CombineVolumeChangeResults(volumeResult, muteResult);
    }

    private static unsafe LidGuardOperationResult RestoreDefaultRenderDeviceStateCore(SystemAudioVolumeState state)
    {
        var endpointVolumeResult = CreateDefaultAudioEndpointVolume();
        if (!endpointVolumeResult.Succeeded) return endpointVolumeResult.ToNonGenericResult();

        using var endpointVolumeHandle = endpointVolumeResult.Value;
        var volumeResult = SetMasterVolumeScalar(endpointVolumeHandle.Pointer, state.MasterVolumeScalar);
        var muteResult = SetMute(endpointVolumeHandle.Pointer, state.IsMuted);
        return CombineVolumeChangeResults(volumeResult, muteResult);
    }

    private static LidGuardOperationResult CombineVolumeChangeResults(params LidGuardOperationResult[] results)
    {
        var failedResults = results.Where(static result => !result.Succeeded).ToArray();
        if (failedResults.Length == 0) return LidGuardOperationResult.Success();

        var message = string.Join(" ", failedResults.Select(static result => result.Message));
        var nativeErrorCode = failedResults.FirstOrDefault(static result => result.NativeErrorCode != 0)?.NativeErrorCode ?? 0;
        return LidGuardOperationResult.Failure(message, nativeErrorCode);
    }

    private static unsafe LidGuardOperationResult<ComInterfaceHandle<IAudioEndpointVolume>> CreateDefaultAudioEndpointVolume()
    {
        var initializeResult = ComApartment.Initialize(out var comApartment);
        if (!initializeResult.Succeeded) return LidGuardOperationResult<ComInterfaceHandle<IAudioEndpointVolume>>.Failure(initializeResult.Message, initializeResult.NativeErrorCode);

        var shouldDisposeComApartment = true;
        try
        {
            var enumeratorResult = CreateMmDeviceEnumerator();
            if (!enumeratorResult.Succeeded) return LidGuardOperationResult<ComInterfaceHandle<IAudioEndpointVolume>>.Failure(enumeratorResult.Message, enumeratorResult.NativeErrorCode);

            using var enumeratorHandle = enumeratorResult.Value;
            var endpointResult = GetDefaultAudioEndpoint(enumeratorHandle.Pointer);
            if (!endpointResult.Succeeded) return LidGuardOperationResult<ComInterfaceHandle<IAudioEndpointVolume>>.Failure(endpointResult.Message, endpointResult.NativeErrorCode);

            using var endpointHandle = endpointResult.Value;
            var endpointVolumeResult = ActivateAudioEndpointVolume(endpointHandle.Pointer, comApartment);
            if (!endpointVolumeResult.Succeeded) return endpointVolumeResult;

            shouldDisposeComApartment = false;
            return endpointVolumeResult;
        }
        finally
        {
            if (shouldDisposeComApartment) comApartment.Dispose();
        }
    }

    private static unsafe LidGuardOperationResult<ComInterfaceHandle<IMMDeviceEnumerator>> CreateMmDeviceEnumerator()
    {
        void* enumeratorPointer = null;
        var result = PInvoke.CoCreateInstance(
            s_mmDeviceEnumeratorClassIdentifier,
            null,
            CLSCTX.CLSCTX_INPROC_SERVER,
            IMMDeviceEnumerator.IID_Guid,
            out enumeratorPointer);
        if (Failed(result)) return LidGuardOperationResult<ComInterfaceHandle<IMMDeviceEnumerator>>.Failure("Failed to create the Windows audio device enumerator.", (int)result);

        return LidGuardOperationResult<ComInterfaceHandle<IMMDeviceEnumerator>>.Success(new ComInterfaceHandle<IMMDeviceEnumerator>((IMMDeviceEnumerator*)enumeratorPointer));
    }

    private static unsafe LidGuardOperationResult<ComInterfaceHandle<IMMDevice>> GetDefaultAudioEndpoint(IMMDeviceEnumerator* enumeratorPointer)
    {
        IMMDevice* endpointPointer = null;
        var result = enumeratorPointer->GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, &endpointPointer);
        if (Failed(result)) return LidGuardOperationResult<ComInterfaceHandle<IMMDevice>>.Failure("Failed to get the default Windows render audio endpoint.", (int)result);

        return LidGuardOperationResult<ComInterfaceHandle<IMMDevice>>.Success(new ComInterfaceHandle<IMMDevice>(endpointPointer));
    }

    private static unsafe LidGuardOperationResult<ComInterfaceHandle<IAudioEndpointVolume>> ActivateAudioEndpointVolume(IMMDevice* endpointPointer, ComApartment comApartment)
    {
        void* endpointVolumePointer = null;
        var result = endpointPointer->Activate(IAudioEndpointVolume.IID_Guid, CLSCTX.CLSCTX_INPROC_SERVER, null, out endpointVolumePointer);
        if (Failed(result)) return LidGuardOperationResult<ComInterfaceHandle<IAudioEndpointVolume>>.Failure("Failed to activate the Windows audio endpoint volume interface.", (int)result);

        return LidGuardOperationResult<ComInterfaceHandle<IAudioEndpointVolume>>.Success(new ComInterfaceHandle<IAudioEndpointVolume>((IAudioEndpointVolume*)endpointVolumePointer, comApartment));
    }

    private static unsafe LidGuardOperationResult GetMasterVolumeScalar(IAudioEndpointVolume* endpointVolumePointer, out float masterVolumeScalar)
    {
        masterVolumeScalar = 0.0f;
        var result = endpointVolumePointer->GetMasterVolumeLevelScalar(out var capturedMasterVolumeScalar);
        if (Failed(result)) return LidGuardOperationResult.Failure("Failed to capture the current Windows master volume.", (int)result);

        masterVolumeScalar = capturedMasterVolumeScalar;
        return LidGuardOperationResult.Success();
    }

    private static unsafe LidGuardOperationResult SetMasterVolumeScalar(IAudioEndpointVolume* endpointVolumePointer, float masterVolumeScalar)
    {
        var result = endpointVolumePointer->SetMasterVolumeLevelScalar(masterVolumeScalar, null);
        if (Failed(result)) return LidGuardOperationResult.Failure("Failed to set the Windows master volume.", (int)result);

        return LidGuardOperationResult.Success();
    }

    private static unsafe LidGuardOperationResult GetMute(IAudioEndpointVolume* endpointVolumePointer, out bool isMuted)
    {
        var result = endpointVolumePointer->GetMute(out var muteValue);
        if (Failed(result))
        {
            isMuted = false;
            return LidGuardOperationResult.Failure("Failed to capture the current Windows audio mute state.", (int)result);
        }

        isMuted = muteValue;
        return LidGuardOperationResult.Success();
    }

    private static unsafe LidGuardOperationResult SetMute(IAudioEndpointVolume* endpointVolumePointer, bool isMuted)
    {
        var result = endpointVolumePointer->SetMute(new BOOL(isMuted ? 1 : 0), null);
        if (Failed(result)) return LidGuardOperationResult.Failure("Failed to set the Windows audio mute state.", (int)result);

        return LidGuardOperationResult.Success();
    }

    private static bool Failed(HRESULT result) => !result.Succeeded;

    private readonly struct ComApartment(bool shouldUninitialize) : IDisposable
    {
        public static LidGuardOperationResult Initialize(out ComApartment comApartment)
        {
            var result = PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
            if ((int)result is Success or SuccessFalse)
            {
                comApartment = new ComApartment(true);
                return LidGuardOperationResult.Success();
            }

            if ((int)result == RpcChangedMode)
            {
                comApartment = new ComApartment(false);
                return LidGuardOperationResult.Success();
            }

            comApartment = new ComApartment(false);
            return LidGuardOperationResult.Failure("Failed to initialize COM for Windows audio volume control.", (int)result);
        }

        public void Dispose()
        {
            if (shouldUninitialize) PInvoke.CoUninitialize();
        }
    }

    private sealed unsafe class ComInterfaceHandle<TComInterface>(TComInterface* pointer, ComApartment comApartment = default) : IDisposable
        where TComInterface : unmanaged
    {
        private TComInterface* _pointer = pointer;
        private bool _disposed;

        public TComInterface* Pointer => _pointer;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            if (_pointer != null)
            {
                ((IUnknown*)_pointer)->Release();
                _pointer = null;
            }

            comApartment.Dispose();
        }
    }
}

internal static class AudioVolumeOperationResultExtensions
{
    public static LidGuardOperationResult ToNonGenericResult<TValue>(this LidGuardOperationResult<TValue> result)
        => result.Succeeded ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(result.Message, result.NativeErrorCode);
}

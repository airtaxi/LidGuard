using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Settings;

namespace LidGuardLib.Audio;

[SupportedOSPlatform("windows6.1")]
public sealed partial class SystemAudioVolumeController : ISystemAudioVolumeController
{
    private const int Success = 0;
    private const int SuccessFalse = 1;
    private const int RpcChangedMode = unchecked((int)0x80010106);
    private const uint ClassContextInProcessServer = 0x1;
    private const uint CoInitializeMultithreaded = 0x0;
    private const int AudioDataFlowRender = 0;
    private const int AudioRoleConsole = 0;
    private static readonly Guid s_mmDeviceEnumeratorClassIdentifier = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid s_mmDeviceEnumeratorInterfaceIdentifier = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid s_audioEndpointVolumeInterfaceIdentifier = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public LidGuardOperationResult<SystemAudioVolumeState> CaptureDefaultRenderDeviceState()
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

    public LidGuardOperationResult ApplyDefaultRenderDeviceVolumeOverride(int volumeOverridePercent)
    {
        if (!LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(volumeOverridePercent)) return LidGuardOperationResult.Failure($"The volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.");

        var endpointVolumeResult = CreateDefaultAudioEndpointVolume();
        if (!endpointVolumeResult.Succeeded) return endpointVolumeResult.ToNonGenericResult();

        using var endpointVolumeHandle = endpointVolumeResult.Value;
        var volumeScalar = volumeOverridePercent / 100.0f;
        var volumeResult = SetMasterVolumeScalar(endpointVolumeHandle.Pointer, volumeScalar);
        var muteResult = SetMute(endpointVolumeHandle.Pointer, false);
        return CombineVolumeChangeResults(volumeResult, muteResult);
    }

    public LidGuardOperationResult RestoreDefaultRenderDeviceState(SystemAudioVolumeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

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

    private static unsafe LidGuardOperationResult<ComInterfaceHandle> CreateDefaultAudioEndpointVolume()
    {
        var initializeResult = ComApartment.Initialize(out var comApartment);
        if (!initializeResult.Succeeded) return LidGuardOperationResult<ComInterfaceHandle>.Failure(initializeResult.Message, initializeResult.NativeErrorCode);

        var shouldDisposeComApartment = true;
        try
        {
            var enumeratorResult = CreateMmDeviceEnumerator();
            if (!enumeratorResult.Succeeded) return LidGuardOperationResult<ComInterfaceHandle>.Failure(enumeratorResult.Message, enumeratorResult.NativeErrorCode);

            using var enumeratorHandle = enumeratorResult.Value;
            var endpointResult = GetDefaultAudioEndpoint(enumeratorHandle.Pointer);
            if (!endpointResult.Succeeded) return LidGuardOperationResult<ComInterfaceHandle>.Failure(endpointResult.Message, endpointResult.NativeErrorCode);

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

    private static unsafe LidGuardOperationResult<ComInterfaceHandle> CreateMmDeviceEnumerator()
    {
        void* enumeratorPointer = null;
        var classIdentifier = s_mmDeviceEnumeratorClassIdentifier;
        var interfaceIdentifier = s_mmDeviceEnumeratorInterfaceIdentifier;
        var result = CoCreateInstance(
            &classIdentifier,
            null,
            ClassContextInProcessServer,
            &interfaceIdentifier,
            &enumeratorPointer);
        if (Failed(result)) return LidGuardOperationResult<ComInterfaceHandle>.Failure("Failed to create the Windows audio device enumerator.", result);

        return LidGuardOperationResult<ComInterfaceHandle>.Success(new ComInterfaceHandle((nint)enumeratorPointer));
    }

    private static unsafe LidGuardOperationResult<ComInterfaceHandle> GetDefaultAudioEndpoint(nint enumeratorPointer)
    {
        void* endpointPointer = null;
        var virtualTable = *(void***)enumeratorPointer;
        var getDefaultAudioEndpoint = (delegate* unmanaged[Stdcall]<void*, int, int, void**, int>)virtualTable[4];
        var result = getDefaultAudioEndpoint((void*)enumeratorPointer, AudioDataFlowRender, AudioRoleConsole, &endpointPointer);
        if (Failed(result)) return LidGuardOperationResult<ComInterfaceHandle>.Failure("Failed to get the default Windows render audio endpoint.", result);

        return LidGuardOperationResult<ComInterfaceHandle>.Success(new ComInterfaceHandle((nint)endpointPointer));
    }

    private static unsafe LidGuardOperationResult<ComInterfaceHandle> ActivateAudioEndpointVolume(nint endpointPointer, ComApartment comApartment)
    {
        void* endpointVolumePointer = null;
        var interfaceIdentifier = s_audioEndpointVolumeInterfaceIdentifier;
        var virtualTable = *(void***)endpointPointer;
        var activate = (delegate* unmanaged[Stdcall]<void*, Guid*, uint, void*, void**, int>)virtualTable[3];
        var result = activate((void*)endpointPointer, &interfaceIdentifier, ClassContextInProcessServer, null, &endpointVolumePointer);
        if (Failed(result)) return LidGuardOperationResult<ComInterfaceHandle>.Failure("Failed to activate the Windows audio endpoint volume interface.", result);

        return LidGuardOperationResult<ComInterfaceHandle>.Success(new ComInterfaceHandle((nint)endpointVolumePointer, comApartment));
    }

    private static unsafe LidGuardOperationResult GetMasterVolumeScalar(nint endpointVolumePointer, out float masterVolumeScalar)
    {
        masterVolumeScalar = 0.0f;
        var capturedMasterVolumeScalar = 0.0f;
        var virtualTable = *(void***)endpointVolumePointer;
        var getMasterVolumeLevelScalar = (delegate* unmanaged[Stdcall]<void*, float*, int>)virtualTable[9];
        var result = getMasterVolumeLevelScalar((void*)endpointVolumePointer, &capturedMasterVolumeScalar);
        if (Failed(result)) return LidGuardOperationResult.Failure("Failed to capture the current Windows master volume.", result);

        masterVolumeScalar = capturedMasterVolumeScalar;
        return LidGuardOperationResult.Success();
    }

    private static unsafe LidGuardOperationResult SetMasterVolumeScalar(nint endpointVolumePointer, float masterVolumeScalar)
    {
        var virtualTable = *(void***)endpointVolumePointer;
        var setMasterVolumeLevelScalar = (delegate* unmanaged[Stdcall]<void*, float, Guid*, int>)virtualTable[7];
        var result = setMasterVolumeLevelScalar((void*)endpointVolumePointer, masterVolumeScalar, null);
        if (Failed(result)) return LidGuardOperationResult.Failure("Failed to set the Windows master volume.", result);

        return LidGuardOperationResult.Success();
    }

    private static unsafe LidGuardOperationResult GetMute(nint endpointVolumePointer, out bool isMuted)
    {
        var muteValue = 0;
        var virtualTable = *(void***)endpointVolumePointer;
        var getMute = (delegate* unmanaged[Stdcall]<void*, int*, int>)virtualTable[15];
        var result = getMute((void*)endpointVolumePointer, &muteValue);
        if (Failed(result))
        {
            isMuted = false;
            return LidGuardOperationResult.Failure("Failed to capture the current Windows audio mute state.", result);
        }

        isMuted = muteValue != 0;
        return LidGuardOperationResult.Success();
    }

    private static unsafe LidGuardOperationResult SetMute(nint endpointVolumePointer, bool isMuted)
    {
        var virtualTable = *(void***)endpointVolumePointer;
        var setMute = (delegate* unmanaged[Stdcall]<void*, int, Guid*, int>)virtualTable[14];
        var result = setMute((void*)endpointVolumePointer, isMuted ? 1 : 0, null);
        if (Failed(result)) return LidGuardOperationResult.Failure("Failed to set the Windows audio mute state.", result);

        return LidGuardOperationResult.Success();
    }

    private static bool Failed(int result) => result < 0;

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(
        Guid* classIdentifier,
        void* outerUnknown,
        uint classContext,
        Guid* interfaceIdentifier,
        void** interfacePointer);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint coInitializeOption);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    private readonly struct ComApartment(bool shouldUninitialize) : IDisposable
    {
        public static LidGuardOperationResult Initialize(out ComApartment comApartment)
        {
            var result = CoInitializeEx(IntPtr.Zero, CoInitializeMultithreaded);
            if (result is Success or SuccessFalse)
            {
                comApartment = new ComApartment(true);
                return LidGuardOperationResult.Success();
            }

            if (result == RpcChangedMode)
            {
                comApartment = new ComApartment(false);
                return LidGuardOperationResult.Success();
            }

            comApartment = new ComApartment(false);
            return LidGuardOperationResult.Failure("Failed to initialize COM for Windows audio volume control.", result);
        }

        public void Dispose()
        {
            if (shouldUninitialize) CoUninitialize();
        }
    }

    private sealed class ComInterfaceHandle(nint pointer, ComApartment comApartment = default) : IDisposable
    {
        private nint _pointer = pointer;
        private bool _disposed;

        public nint Pointer => _pointer;

        public unsafe void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            if (_pointer != 0)
            {
                var virtualTable = *(void***)_pointer;
                var release = (delegate* unmanaged[Stdcall]<void*, uint>)virtualTable[2];
                release((void*)_pointer);
                _pointer = 0;
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

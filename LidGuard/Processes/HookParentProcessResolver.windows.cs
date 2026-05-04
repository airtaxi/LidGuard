using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using WdkPInvoke = Windows.Wdk.PInvoke;
using WdkProcessInformationClass = Windows.Wdk.System.Threading.PROCESSINFOCLASS;

namespace LidGuard.Processes;

#pragma warning disable CA1416
internal static partial class HookParentProcessResolver
{
    private static partial HookParentProcessInfoReader CreateProcessInfoReader() => new WindowsHookParentProcessInfoReader();

    private sealed class WindowsHookParentProcessInfoReader : HookParentProcessInfoReader
    {
        public override bool TryReadProcessInfo(int processIdentifier, out HookParentProcessInfo processInfo)
        {
            processInfo = default;

            var accessRights = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ;
            using var processHandle = PInvoke.OpenProcess_SafeHandle(accessRights, false, (uint)processIdentifier);
            if (processHandle.IsInvalid) return false;
            if (!RemoteProcessParametersReader.TryReadParentProcessIdentifier(processHandle, out var parentProcessIdentifier)) return false;

            var processName = GetProcessName(processIdentifier);
            RemoteProcessParametersReader.TryReadCommandLine(processHandle, out var commandLine);
            processInfo = new HookParentProcessInfo(processIdentifier, parentProcessIdentifier, processName, commandLine);
            return true;
        }

        private static string GetProcessName(int processIdentifier)
        {
            try
            {
                using var process = Process.GetProcessById(processIdentifier);
                return process.ProcessName;
            }
            catch { return string.Empty; }
        }
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static unsafe partial class RemoteProcessParametersReader
    {
        private static readonly int s_processParametersOffset = IntPtr.Size == 8 ? 0x20 : 0x10;
        private static readonly int s_commandLineOffset = IntPtr.Size == 8 ? 0x70 : 0x40;

        public static bool TryReadParentProcessIdentifier(SafeFileHandle processHandle, out int parentProcessIdentifier)
        {
            parentProcessIdentifier = 0;

            var processBasicInformation = default(PROCESS_BASIC_INFORMATION);
            var returnLength = 0u;
            var status = WdkPInvoke.NtQueryInformationProcess(
                (HANDLE)processHandle.DangerousGetHandle(),
                WdkProcessInformationClass.ProcessBasicInformation,
                &processBasicInformation,
                (uint)sizeof(PROCESS_BASIC_INFORMATION),
                ref returnLength);
            if ((int)status != 0) return false;

            var parentProcessIdentifierValue = (nuint)processBasicInformation.InheritedFromUniqueProcessId;
            if (parentProcessIdentifierValue > int.MaxValue) return false;

            parentProcessIdentifier = (int)parentProcessIdentifierValue;
            return parentProcessIdentifier > 0;
        }

        public static bool TryReadCommandLine(SafeFileHandle processHandle, out string commandLine)
        {
            commandLine = string.Empty;

            var processBasicInformation = default(PROCESS_BASIC_INFORMATION);
            var returnLength = 0u;
            var status = WdkPInvoke.NtQueryInformationProcess(
                (HANDLE)processHandle.DangerousGetHandle(),
                WdkProcessInformationClass.ProcessBasicInformation,
                &processBasicInformation,
                (uint)sizeof(PROCESS_BASIC_INFORMATION),
                ref returnLength);
            if ((int)status != 0) return false;

            var processEnvironmentBlockAddress = (IntPtr)processBasicInformation.PebBaseAddress;
            var processParametersAddress = ReadPointer(processHandle, processEnvironmentBlockAddress + s_processParametersOffset);
            if (processParametersAddress == IntPtr.Zero) return false;

            if (!TryReadStructure(processHandle, processParametersAddress + s_commandLineOffset, out RemoteUnicodeString commandLineString)) return false;
            return TryReadUnicodeString(processHandle, commandLineString, out commandLine);
        }

        private static IntPtr ReadPointer(SafeFileHandle processHandle, IntPtr address)
        {
            if (IntPtr.Size == 8) return TryReadStructure(processHandle, address, out long pointerValue) ? (IntPtr)pointerValue : IntPtr.Zero;
            return TryReadStructure(processHandle, address, out int pointerValue32) ? (IntPtr)pointerValue32 : IntPtr.Zero;
        }

        private static bool TryReadUnicodeString(SafeFileHandle processHandle, RemoteUnicodeString unicodeString, out string value)
        {
            value = string.Empty;
            if (unicodeString.Length == 0 || unicodeString.Buffer == IntPtr.Zero) return false;
            if (unicodeString.Length > 32767 * sizeof(char)) return false;

            var characterCount = unicodeString.Length / sizeof(char);
            var buffer = new char[characterCount];
            var byteBuffer = MemoryMarshal.AsBytes(buffer.AsSpan());
            if (!PInvoke.ReadProcessMemory(processHandle, unicodeString.Buffer.ToPointer(), byteBuffer, out var bytesRead)) return false;
            if (bytesRead != (nuint)byteBuffer.Length) return false;

            value = new string(buffer);
            return true;
        }

        private static bool TryReadStructure<TValue>(SafeFileHandle processHandle, IntPtr address, out TValue value)
            where TValue : unmanaged
        {
            value = default;
            if (address == IntPtr.Zero) return false;

            Span<byte> byteBuffer = stackalloc byte[sizeof(TValue)];
            if (!PInvoke.ReadProcessMemory(processHandle, address.ToPointer(), byteBuffer, out var bytesRead)) return false;
            if (bytesRead != (nuint)byteBuffer.Length) return false;

            value = MemoryMarshal.Read<TValue>(byteBuffer);
            return true;
        }

#pragma warning disable CS0649
        private struct RemoteUnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }
#pragma warning restore CS0649
    }
}
#pragma warning restore CA1416

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuardLib.Commons.Processes;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Sessions;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace LidGuardLib.Windows.Processes;

[SupportedOSPlatform("windows6.1")]
public sealed partial class WindowsCommandLineProcessResolver : ICommandLineProcessResolver
{
    private static readonly HashSet<string> s_commandLineProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude",
        "cmd",
        "codex",
        "copilot",
        "dotnet",
        "gh",
        "node",
        "powershell",
        "pwsh"
    };

    private static readonly string[] s_lidGuardUtilityCommandNames =
    [
        "claude-hook",
        "codex-hook",
        "copilot-hook",
        "mcp-server",
        "provider-mcp-server"
    ];

    public LidGuardOperationResult<CommandLineProcessCandidate> FindForWorkingDirectory(string workingDirectory, AgentProvider provider = AgentProvider.Unknown)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("A working directory is required.");

        var normalizedWorkingDirectory = NormalizeDirectory(workingDirectory);
        var candidates = new List<(CommandLineProcessCandidate Candidate, int Score)>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;

                var processName = GetProcessName(process);
                if (string.IsNullOrWhiteSpace(processName)) continue;

                var score = GetProcessScore(provider, processName);
                if (score == 0 && !s_commandLineProcessNames.Contains(processName)) continue;

                if (!TryReadCurrentDirectory(process.Id, out var processWorkingDirectory)) continue;
                if (!DirectoryMatches(normalizedWorkingDirectory, processWorkingDirectory)) continue;
                if (IsLidGuardUtilityProcess(process.Id)) continue;

                var candidate = new CommandLineProcessCandidate
                {
                    ProcessIdentifier = process.Id,
                    ProcessName = processName,
                    WorkingDirectory = processWorkingDirectory,
                    Provider = provider,
                    StartedAt = GetStartedAt(process)
                };

                candidates.Add((candidate, score));
            }
        }

        if (candidates.Count == 0) return LidGuardOperationResult<CommandLineProcessCandidate>.Failure("No command-line process was found for the working directory.");

        var selectedCandidate = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Candidate.StartedAt)
            .First()
            .Candidate;

        return LidGuardOperationResult<CommandLineProcessCandidate>.Success(selectedCandidate);
    }

    private static bool TryReadCurrentDirectory(int processIdentifier, out string workingDirectory)
    {
        workingDirectory = string.Empty;

        var accessRights = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ;
        using var processHandle = PInvoke.OpenProcess_SafeHandle(accessRights, false, (uint)processIdentifier);
        if (processHandle.IsInvalid) return false;

        return RemoteProcessParametersReader.TryReadCurrentDirectory(processHandle, out workingDirectory);
    }

    private static bool TryReadCommandLine(int processIdentifier, out string commandLine)
    {
        commandLine = string.Empty;

        var accessRights = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ;
        using var processHandle = PInvoke.OpenProcess_SafeHandle(accessRights, false, (uint)processIdentifier);
        if (processHandle.IsInvalid) return false;

        return RemoteProcessParametersReader.TryReadCommandLine(processHandle, out commandLine);
    }

    private static string GetProcessName(Process process)
    {
        try { return process.ProcessName; }
        catch { return string.Empty; }
    }

    private static DateTimeOffset GetStartedAt(Process process)
    {
        try { return new DateTimeOffset(process.StartTime); }
        catch { return DateTimeOffset.MinValue; }
    }

    private static int GetProcessScore(AgentProvider provider, string processName)
    {
        if (provider == AgentProvider.Codex && processName.Equals("codex", StringComparison.OrdinalIgnoreCase)) return 100;
        if (provider == AgentProvider.Claude && processName.Equals("claude", StringComparison.OrdinalIgnoreCase)) return 100;
        if (provider == AgentProvider.GitHubCopilot && processName.Contains("copilot", StringComparison.OrdinalIgnoreCase)) return 100;
        if (s_commandLineProcessNames.Contains(processName)) return 50;
        return 0;
    }

    private static bool IsLidGuardUtilityProcess(int processIdentifier)
    {
        if (!TryReadCommandLine(processIdentifier, out var commandLine)) return false;

        return IsLidGuardUtilityCommandLine(commandLine);
    }

    private static bool IsLidGuardUtilityCommandLine(string commandLine)
    {
        if (!commandLine.Contains("lidguard", StringComparison.OrdinalIgnoreCase)) return false;
        return s_lidGuardUtilityCommandNames.Any(commandName => commandLine.Contains(commandName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool DirectoryMatches(string normalizedWorkingDirectory, string processWorkingDirectory)
    {
        var normalizedProcessWorkingDirectory = NormalizeDirectory(processWorkingDirectory);
        return string.Equals(normalizedWorkingDirectory, normalizedProcessWorkingDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string directory)
    {
        var convertedDirectory = directory;
        if (convertedDirectory.StartsWith(@"\??\", StringComparison.Ordinal)) convertedDirectory = convertedDirectory[4..];
        if (convertedDirectory.StartsWith(@"\\?\", StringComparison.Ordinal)) convertedDirectory = convertedDirectory[4..];

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(convertedDirectory));
    }

    private static unsafe partial class RemoteProcessParametersReader
    {
        private const int ProcessBasicInformationClass = 0;
        private static readonly int s_processParametersOffset = IntPtr.Size == 8 ? 0x20 : 0x10;
        private static readonly int s_currentDirectoryOffset = IntPtr.Size == 8 ? 0x38 : 0x24;
        private static readonly int s_commandLineOffset = IntPtr.Size == 8 ? 0x70 : 0x40;

        public static bool TryReadCurrentDirectory(SafeFileHandle processHandle, out string workingDirectory)
        {
            workingDirectory = string.Empty;

            var processBasicInformation = default(ProcessBasicInformation);
            var status = NtQueryInformationProcess(processHandle, ProcessBasicInformationClass, &processBasicInformation, (uint)sizeof(ProcessBasicInformation), out _);
            if (status != 0) return false;

            var processParametersAddress = ReadPointer(processHandle, processBasicInformation.PebBaseAddress + s_processParametersOffset);
            if (processParametersAddress == IntPtr.Zero) return false;

            if (!TryReadStructure(processHandle, processParametersAddress + s_currentDirectoryOffset, out RemoteUnicodeString currentDirectory)) return false;
            return TryReadUnicodeString(processHandle, currentDirectory, out workingDirectory);
        }

        public static bool TryReadCommandLine(SafeFileHandle processHandle, out string commandLine)
        {
            commandLine = string.Empty;

            var processBasicInformation = default(ProcessBasicInformation);
            var status = NtQueryInformationProcess(processHandle, ProcessBasicInformationClass, &processBasicInformation, (uint)sizeof(ProcessBasicInformation), out _);
            if (status != 0) return false;

            var processParametersAddress = ReadPointer(processHandle, processBasicInformation.PebBaseAddress + s_processParametersOffset);
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

        [LibraryImport("ntdll.dll")]
        private static partial int NtQueryInformationProcess(
            SafeHandle processHandle,
            int processInformationClass,
            void* processInformation,
            uint processInformationLength,
            out uint returnLength);

#pragma warning disable CS0649
        private struct ProcessBasicInformation
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2;
            public IntPtr Reserved3;
            public IntPtr UniqueProcessIdentifier;
            public IntPtr Reserved4;
        }

        private struct RemoteUnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }
#pragma warning restore CS0649
    }
}

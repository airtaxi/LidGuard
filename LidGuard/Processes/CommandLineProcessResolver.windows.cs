using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LidGuard.Processes;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Sessions;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using WdkPInvoke = Windows.Wdk.PInvoke;
using WdkProcessInformationClass = Windows.Wdk.System.Threading.PROCESSINFOCLASS;

namespace LidGuard.Processes;

[SupportedOSPlatform("windows6.1")]
public sealed partial class CommandLineProcessResolver : ICommandLineProcessResolver
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

                var isShellHosted = IsShellHostedCandidate(process.Id, processName);
                var score = GetProcessScore(provider, processName, isShellHosted);
                if (score == 0 && !s_commandLineProcessNames.Contains(processName)) continue;

                if (!TryReadCurrentDirectory(process.Id, out var processWorkingDirectory)) continue;
                if (!DirectoryMatches(normalizedWorkingDirectory, processWorkingDirectory)) continue;
                if (IsLidGuardUtilityProcess(process.Id)) continue;

                var candidate = new CommandLineProcessCandidate
                {
                    ProcessIdentifier = process.Id,
                    ProcessName = processName,
                    WorkingDirectory = processWorkingDirectory,
                    IsShellHosted = isShellHosted,
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

    private static int GetProcessScore(AgentProvider provider, string processName, bool isShellHosted)
    {
        if (provider == AgentProvider.Codex)
        {
            if (processName.Equals("codex", StringComparison.OrdinalIgnoreCase)) return isShellHosted ? 200 : 100;
            if (isShellHosted) return 150;
        }

        if (provider == AgentProvider.Claude && processName.Equals("claude", StringComparison.OrdinalIgnoreCase)) return 100;
        if (provider == AgentProvider.GitHubCopilot && processName.Contains("copilot", StringComparison.OrdinalIgnoreCase)) return 100;
        if (s_commandLineProcessNames.Contains(processName)) return 50;
        return 0;
    }

    private static bool IsShellHostedCandidate(int processIdentifier, string processName)
    {
        if (IsShellHostProcessName(processName)) return true;
        if (!TryReadParentProcessIdentifier(processIdentifier, out var parentProcessIdentifier) || parentProcessIdentifier <= 0) return false;

        return TryGetProcessName(parentProcessIdentifier, out var parentProcessName) && IsShellHostProcessName(parentProcessName);
    }

    private static bool IsShellHostProcessName(string processName)
        => processName.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("pwsh", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadParentProcessIdentifier(int processIdentifier, out int parentProcessIdentifier)
    {
        parentProcessIdentifier = 0;

        var accessRights = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION;
        using var processHandle = PInvoke.OpenProcess_SafeHandle(accessRights, false, (uint)processIdentifier);
        if (processHandle.IsInvalid) return false;

        return RemoteProcessParametersReader.TryReadParentProcessIdentifier(processHandle, out parentProcessIdentifier);
    }

    private static bool TryGetProcessName(int processIdentifier, out string processName)
    {
        processName = string.Empty;

        try
        {
            using var process = Process.GetProcessById(processIdentifier);
            processName = GetProcessName(process);
            return !string.IsNullOrWhiteSpace(processName);
        }
        catch
        {
            return false;
        }
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
        private static readonly int s_processParametersOffset = IntPtr.Size == 8 ? 0x20 : 0x10;
        private static readonly int s_currentDirectoryOffset = IntPtr.Size == 8 ? 0x38 : 0x24;
        private static readonly int s_commandLineOffset = IntPtr.Size == 8 ? 0x70 : 0x40;

        public static bool TryReadCurrentDirectory(SafeFileHandle processHandle, out string workingDirectory)
        {
            workingDirectory = string.Empty;

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

            if (!TryReadStructure(processHandle, processParametersAddress + s_currentDirectoryOffset, out RemoteUnicodeString currentDirectory)) return false;
            return TryReadUnicodeString(processHandle, currentDirectory, out workingDirectory);
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

            var parentProcessIdentifierNative = processBasicInformation.InheritedFromUniqueProcessId;
            if (parentProcessIdentifierNative == 0 || parentProcessIdentifierNative > (nuint)int.MaxValue) return false;

            parentProcessIdentifier = (int)parentProcessIdentifierNative;
            return true;
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

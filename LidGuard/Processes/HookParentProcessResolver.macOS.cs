using LidGuard.Platform;

namespace LidGuard.Processes;

internal static partial class HookParentProcessResolver
{
    private static readonly TimeSpan s_processListTimeout = TimeSpan.FromSeconds(5);

    private static partial HookParentProcessInfoReader CreateProcessInfoReader() => new MacOSHookParentProcessInfoReader();

    private sealed class MacOSHookParentProcessInfoReader : HookParentProcessInfoReader
    {
        private readonly Dictionary<int, HookParentProcessInfo> _processes = ReadProcesses();

        public override bool TryReadProcessInfo(int processIdentifier, out HookParentProcessInfo processInfo) => _processes.TryGetValue(processIdentifier, out processInfo);

        private static Dictionary<int, HookParentProcessInfo> ReadProcesses()
        {
            var processes = new Dictionary<int, HookParentProcessInfo>();
            if (!MacOSCommandPathResolver.TryFindExecutable("ps", out var processListPath)) return processes;

            var processListResult = MacOSCommandRunner.Run(processListPath, ["-axo", "pid=,ppid=,comm=,command="], s_processListTimeout);
            if (!processListResult.Succeeded) return processes;

            foreach (var line in processListResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split([' ', '\t'], 4, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (fields.Length < 3) continue;
                if (!int.TryParse(fields[0], out var processIdentifier)) continue;
                if (!int.TryParse(fields[1], out var parentProcessIdentifier)) continue;

                var processName = NormalizeProcessName(Path.GetFileName(fields[2]));
                if (string.IsNullOrWhiteSpace(processName)) processName = NormalizeProcessName(fields[2]);
                var commandLine = fields.Length >= 4 ? fields[3] : string.Empty;
                processes[processIdentifier] = new HookParentProcessInfo(processIdentifier, parentProcessIdentifier, processName, commandLine);
            }

            return processes;
        }
    }
}

namespace LidGuard.Processes;

internal static partial class HookParentProcessResolver
{
    private const string ProcessRootPath = "/proc";

    private static partial HookParentProcessInfoReader CreateProcessInfoReader() => new LinuxHookParentProcessInfoReader();

    private sealed class LinuxHookParentProcessInfoReader : HookParentProcessInfoReader
    {
        public override bool TryReadProcessInfo(int processIdentifier, out HookParentProcessInfo processInfo)
        {
            processInfo = default;
            if (!TryReadParentProcessIdentifier(processIdentifier, out var parentProcessIdentifier)) return false;

            var processName = ReadProcessName(processIdentifier);
            TryReadCommandLine(processIdentifier, out var commandLine);
            processInfo = new HookParentProcessInfo(processIdentifier, parentProcessIdentifier, processName, commandLine);
            return true;
        }

        private static bool TryReadParentProcessIdentifier(int processIdentifier, out int parentProcessIdentifier)
        {
            parentProcessIdentifier = 0;

            try
            {
                var statText = File.ReadAllText(Path.Combine(ProcessRootPath, processIdentifier.ToString(), "stat"));
                var processNameEndIndex = statText.LastIndexOf(')');
                if (processNameEndIndex < 0 || processNameEndIndex + 2 >= statText.Length) return false;

                var remainingFields = statText[(processNameEndIndex + 2)..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (remainingFields.Length < 2) return false;
                return int.TryParse(remainingFields[1], out parentProcessIdentifier) && parentProcessIdentifier > 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
        }

        private static string ReadProcessName(int processIdentifier)
        {
            try
            {
                var processName = File.ReadAllText(Path.Combine(ProcessRootPath, processIdentifier.ToString(), "comm")).Trim();
                return string.IsNullOrWhiteSpace(processName) ? string.Empty : processName;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return string.Empty; }
        }

        private static bool TryReadCommandLine(int processIdentifier, out string commandLine)
        {
            commandLine = string.Empty;

            try
            {
                var commandLineText = File.ReadAllText(Path.Combine(ProcessRootPath, processIdentifier.ToString(), "cmdline"));
                commandLine = commandLineText.Replace('\0', ' ').Trim();
                return !string.IsNullOrWhiteSpace(commandLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
        }
    }
}

using LidGuardLib.Commons.Sessions;

namespace LidGuard.Runtime;

internal sealed class CodexSoftLockTranscriptMonitor(Func<CodexTranscriptActivityThresholdReachedContext, Task> transcriptActivityThresholdReachedAsync)
{
    private const int RequiredActualContentIncreaseCount = 5;
    private readonly object _gate = new();
    private readonly Dictionary<LidGuardSessionKey, MonitoredCodexSessionState> _monitoredCodexSessions = [];

    public CodexTranscriptMonitoringRegistrationResult RegisterOrUpdateSession(
        string sessionIdentifier,
        string workingDirectory,
        string transcriptPath)
    {
        var sessionKey = new LidGuardSessionKey(AgentProvider.Codex, sessionIdentifier);
        var resolvedTranscriptPath = ResolveTranscriptPath(sessionIdentifier, transcriptPath, out var resolutionMessage);

        lock (_gate)
        {
            RemoveSessionInsideGate(sessionKey);
            if (string.IsNullOrWhiteSpace(resolvedTranscriptPath))
            {
                return new CodexTranscriptMonitoringRegistrationResult
                {
                    Message = resolutionMessage
                };
            }

            var transcriptDirectoryPath = Path.GetDirectoryName(resolvedTranscriptPath);
            var transcriptFileName = Path.GetFileName(resolvedTranscriptPath);
            if (string.IsNullOrWhiteSpace(transcriptDirectoryPath) || string.IsNullOrWhiteSpace(transcriptFileName))
            {
                return new CodexTranscriptMonitoringRegistrationResult
                {
                    ResolvedTranscriptPath = resolvedTranscriptPath,
                    Message = $"Skipped Codex transcript monitoring because '{resolvedTranscriptPath}' is not a valid file path."
                };
            }

            if (!Directory.Exists(transcriptDirectoryPath))
            {
                return new CodexTranscriptMonitoringRegistrationResult
                {
                    ResolvedTranscriptPath = resolvedTranscriptPath,
                    Message = $"Skipped Codex transcript monitoring because transcript directory '{transcriptDirectoryPath}' does not exist."
                };
            }

            var fileSystemWatcher = new FileSystemWatcher(transcriptDirectoryPath, transcriptFileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            fileSystemWatcher.Changed += (_, _) => HandleTranscriptFileChanged(sessionKey);
            fileSystemWatcher.Created += (_, _) => HandleTranscriptFileChanged(sessionKey);
            fileSystemWatcher.Renamed += (_, _) => HandleTranscriptFileChanged(sessionKey);
            fileSystemWatcher.EnableRaisingEvents = true;

            _monitoredCodexSessions[sessionKey] = new MonitoredCodexSessionState
            {
                SessionKey = sessionKey,
                WorkingDirectory = workingDirectory,
                TranscriptPath = resolvedTranscriptPath,
                FileSystemWatcher = fileSystemWatcher,
                LastObservedTranscriptLength = GetCurrentTranscriptLength(resolvedTranscriptPath),
                ActualContentIncreaseCount = 0,
                IsSoftLockArmed = false
            };
        }

        return new CodexTranscriptMonitoringRegistrationResult
        {
            ResolvedTranscriptPath = resolvedTranscriptPath,
            MonitoringEnabled = true,
            Message = $"Watching Codex transcript file '{resolvedTranscriptPath}'. {resolutionMessage}".Trim()
        };
    }

    public void ArmSessionSoftLock(LidGuardSessionKey sessionKey)
    {
        lock (_gate)
        {
            if (!_monitoredCodexSessions.TryGetValue(sessionKey, out var monitoredCodexSessionState)) return;

            monitoredCodexSessionState.IsSoftLockArmed = true;
            monitoredCodexSessionState.ActualContentIncreaseCount = 0;
            monitoredCodexSessionState.LastObservedTranscriptLength = GetCurrentTranscriptLength(monitoredCodexSessionState.TranscriptPath);
        }
    }

    public void ResetSession(LidGuardSessionKey sessionKey)
    {
        lock (_gate)
        {
            if (!_monitoredCodexSessions.TryGetValue(sessionKey, out var monitoredCodexSessionState)) return;

            monitoredCodexSessionState.IsSoftLockArmed = false;
            monitoredCodexSessionState.ActualContentIncreaseCount = 0;
            monitoredCodexSessionState.LastObservedTranscriptLength = GetCurrentTranscriptLength(monitoredCodexSessionState.TranscriptPath);
        }
    }

    public void RemoveSession(LidGuardSessionKey sessionKey)
    {
        lock (_gate) RemoveSessionInsideGate(sessionKey);
    }

    private void HandleTranscriptFileChanged(LidGuardSessionKey sessionKey)
    {
        CodexTranscriptActivityThresholdReachedContext transcriptActivityThresholdReachedContext = null;

        lock (_gate)
        {
            if (!_monitoredCodexSessions.TryGetValue(sessionKey, out var monitoredCodexSessionState)) return;

            var currentTranscriptLength = GetCurrentTranscriptLength(monitoredCodexSessionState.TranscriptPath);
            if (currentTranscriptLength < monitoredCodexSessionState.LastObservedTranscriptLength)
            {
                monitoredCodexSessionState.LastObservedTranscriptLength = currentTranscriptLength;
                monitoredCodexSessionState.ActualContentIncreaseCount = 0;
                return;
            }

            if (currentTranscriptLength == monitoredCodexSessionState.LastObservedTranscriptLength) return;

            monitoredCodexSessionState.LastObservedTranscriptLength = currentTranscriptLength;
            if (!monitoredCodexSessionState.IsSoftLockArmed) return;

            monitoredCodexSessionState.ActualContentIncreaseCount++;
            if (monitoredCodexSessionState.ActualContentIncreaseCount < RequiredActualContentIncreaseCount) return;

            monitoredCodexSessionState.IsSoftLockArmed = false;
            var actualContentIncreaseCount = monitoredCodexSessionState.ActualContentIncreaseCount;
            monitoredCodexSessionState.ActualContentIncreaseCount = 0;

            transcriptActivityThresholdReachedContext = new CodexTranscriptActivityThresholdReachedContext
            {
                SessionKey = monitoredCodexSessionState.SessionKey,
                WorkingDirectory = monitoredCodexSessionState.WorkingDirectory,
                TranscriptPath = monitoredCodexSessionState.TranscriptPath,
                ActualContentIncreaseCount = actualContentIncreaseCount
            };
        }

        if (transcriptActivityThresholdReachedContext is null) return;
        _ = NotifyTranscriptActivityThresholdReachedAsync(transcriptActivityThresholdReachedContext, transcriptActivityThresholdReachedAsync);
    }

    private static async Task NotifyTranscriptActivityThresholdReachedAsync(
        CodexTranscriptActivityThresholdReachedContext transcriptActivityThresholdReachedContext,
        Func<CodexTranscriptActivityThresholdReachedContext, Task> transcriptActivityThresholdReachedAsync)
    {
        try
        {
            await transcriptActivityThresholdReachedAsync(transcriptActivityThresholdReachedContext);
        }
        catch
        {
        }
    }

    private void RemoveSessionInsideGate(LidGuardSessionKey sessionKey)
    {
        if (!_monitoredCodexSessions.Remove(sessionKey, out var monitoredCodexSessionState)) return;
        monitoredCodexSessionState.FileSystemWatcher.Dispose();
    }

    private static string ResolveTranscriptPath(string sessionIdentifier, string transcriptPath, out string resolutionMessage)
    {
        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            var normalizedTranscriptPath = NormalizePath(transcriptPath);
            resolutionMessage = $"Using hook transcript_path '{normalizedTranscriptPath}'.";
            return normalizedTranscriptPath;
        }

        var codexSessionsDirectoryPath = GetCodexSessionsDirectoryPath();
        if (string.IsNullOrWhiteSpace(codexSessionsDirectoryPath) || !Directory.Exists(codexSessionsDirectoryPath))
        {
            resolutionMessage = "Skipped Codex transcript monitoring because the Codex sessions directory could not be found.";
            return string.Empty;
        }

        try
        {
            var matchingTranscriptPaths = Directory
                .EnumerateFiles(codexSessionsDirectoryPath, "*.jsonl", SearchOption.AllDirectories)
                .Where(candidateTranscriptPath => candidateTranscriptPath.IndexOf(sessionIdentifier, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(2)
                .Select(NormalizePath)
                .ToArray();

            if (matchingTranscriptPaths.Length == 1)
            {
                resolutionMessage = $"Resolved transcript path by session-id fallback to '{matchingTranscriptPaths[0]}'.";
                return matchingTranscriptPaths[0];
            }

            resolutionMessage = matchingTranscriptPaths.Length == 0
                ? $"Skipped Codex transcript monitoring because hook input did not include transcript_path and no matching transcript file was found for session '{sessionIdentifier}'."
                : $"Skipped Codex transcript monitoring because hook input did not include transcript_path and multiple matching transcript files were found for session '{sessionIdentifier}'.";
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
        {
            resolutionMessage = $"Skipped Codex transcript monitoring because transcript lookup failed: {exception.Message}";
            return string.Empty;
        }
    }

    private static string GetCodexSessionsDirectoryPath()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath)) return string.Empty;
        return Path.Combine(userProfilePath, ".codex", "sessions");
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path.Trim()); }
        catch { return path.Trim(); }
    }

    private static long GetCurrentTranscriptLength(string transcriptPath)
    {
        try
        {
            var transcriptFileInfo = new FileInfo(transcriptPath);
            transcriptFileInfo.Refresh();
            return transcriptFileInfo.Exists ? transcriptFileInfo.Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException)
        {
            return 0;
        }
    }

    private sealed class MonitoredCodexSessionState
    {
        public required LidGuardSessionKey SessionKey { get; init; }

        public required string WorkingDirectory { get; init; }

        public required string TranscriptPath { get; init; }

        public required FileSystemWatcher FileSystemWatcher { get; init; }

        public long LastObservedTranscriptLength { get; set; }

        public int ActualContentIncreaseCount { get; set; }

        public bool IsSoftLockArmed { get; set; }
    }
}

internal sealed class CodexTranscriptMonitoringRegistrationResult
{
    public string ResolvedTranscriptPath { get; init; } = string.Empty;

    public bool MonitoringEnabled { get; init; }

    public string Message { get; init; } = string.Empty;
}

internal sealed class CodexTranscriptActivityThresholdReachedContext
{
    public required LidGuardSessionKey SessionKey { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string TranscriptPath { get; init; }

    public int ActualContentIncreaseCount { get; init; }
}

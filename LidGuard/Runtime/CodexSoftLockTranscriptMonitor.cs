using System.Text.Json;
using LidGuardLib.Commons.Sessions;

namespace LidGuard.Runtime;

internal sealed class CodexSoftLockTranscriptMonitor(
    Func<CodexTranscriptActivityDetectedContext, Task> transcriptActivityDetectedAsync,
    Func<CodexTranscriptTurnAbortedContext, Task> transcriptTurnAbortedAsync)
{
    private const int TranscriptPollingIntervalMilliseconds = 1000;
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

            var pollingTimer = new Timer(_ => HandleTranscriptFileChanged(sessionKey), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var initialTranscriptObservation = GetCurrentTranscriptObservation(resolvedTranscriptPath);
            _monitoredCodexSessions[sessionKey] = new MonitoredCodexSessionState
            {
                SessionKey = sessionKey,
                WorkingDirectory = workingDirectory,
                TranscriptPath = resolvedTranscriptPath,
                FileSystemWatcher = fileSystemWatcher,
                PollingTimer = pollingTimer,
                LastObservedTranscriptLength = initialTranscriptObservation.Length,
                LastObservedTranscriptLastWriteTimeUtc = initialTranscriptObservation.LastWriteTimeUtc
            };
            pollingTimer.Change(
                TimeSpan.FromMilliseconds(TranscriptPollingIntervalMilliseconds),
                TimeSpan.FromMilliseconds(TranscriptPollingIntervalMilliseconds));
        }

        return new CodexTranscriptMonitoringRegistrationResult
        {
            ResolvedTranscriptPath = resolvedTranscriptPath,
            MonitoringEnabled = true,
            Message = $"Watching Codex transcript file '{resolvedTranscriptPath}'. {resolutionMessage}".Trim()
        };
    }

    public void ArmSessionSoftLock(LidGuardSessionKey sessionKey) => ResetSessionObservationBaseline(sessionKey);

    public void ResetSession(LidGuardSessionKey sessionKey) => ResetSessionObservationBaseline(sessionKey);

    public void RemoveSession(LidGuardSessionKey sessionKey)
    {
        lock (_gate) RemoveSessionInsideGate(sessionKey);
    }

    private void HandleTranscriptFileChanged(LidGuardSessionKey sessionKey)
    {
        CodexTranscriptActivityDetectedContext transcriptActivityDetectedContext = null;
        CodexTranscriptTurnAbortedContext transcriptTurnAbortedContext = null;

        lock (_gate)
        {
            if (!_monitoredCodexSessions.TryGetValue(sessionKey, out var monitoredCodexSessionState)) return;
            if (monitoredCodexSessionState.IsTurnAbortedObserved) return;

            var currentTranscriptObservation = GetCurrentTranscriptObservation(monitoredCodexSessionState.TranscriptPath);
            var transcriptLengthIncreased = currentTranscriptObservation.Length > monitoredCodexSessionState.LastObservedTranscriptLength;
            var transcriptLastWriteTimeAdvanced = currentTranscriptObservation.LastWriteTimeUtc > monitoredCodexSessionState.LastObservedTranscriptLastWriteTimeUtc;
            if (!transcriptLengthIncreased && !transcriptLastWriteTimeAdvanced)
            {
                if (currentTranscriptObservation.Length < monitoredCodexSessionState.LastObservedTranscriptLength)
                {
                    monitoredCodexSessionState.LastObservedTranscriptLength = currentTranscriptObservation.Length;
                    monitoredCodexSessionState.LastObservedTranscriptLastWriteTimeUtc = currentTranscriptObservation.LastWriteTimeUtc;
                }

                return;
            }

            monitoredCodexSessionState.LastObservedTranscriptLength = currentTranscriptObservation.Length;
            monitoredCodexSessionState.LastObservedTranscriptLastWriteTimeUtc = currentTranscriptObservation.LastWriteTimeUtc;

            if (IsLastTranscriptLineTurnAborted(monitoredCodexSessionState.TranscriptPath))
            {
                monitoredCodexSessionState.IsTurnAbortedObserved = true;
                transcriptTurnAbortedContext = new CodexTranscriptTurnAbortedContext
                {
                    SessionKey = monitoredCodexSessionState.SessionKey,
                    WorkingDirectory = monitoredCodexSessionState.WorkingDirectory,
                    TranscriptPath = monitoredCodexSessionState.TranscriptPath
                };
            }
            else
            {
                transcriptActivityDetectedContext = new CodexTranscriptActivityDetectedContext
                {
                    SessionKey = monitoredCodexSessionState.SessionKey,
                    WorkingDirectory = monitoredCodexSessionState.WorkingDirectory,
                    TranscriptPath = monitoredCodexSessionState.TranscriptPath
                };
            }
        }

        if (transcriptTurnAbortedContext is not null)
        {
            _ = NotifyTranscriptTurnAbortedAsync(transcriptTurnAbortedContext, transcriptTurnAbortedAsync);
            return;
        }

        if (transcriptActivityDetectedContext is null) return;
        _ = NotifyTranscriptActivityDetectedAsync(transcriptActivityDetectedContext, transcriptActivityDetectedAsync);
    }

    private static async Task NotifyTranscriptActivityDetectedAsync(
        CodexTranscriptActivityDetectedContext transcriptActivityDetectedContext,
        Func<CodexTranscriptActivityDetectedContext, Task> transcriptActivityDetectedAsync)
    {
        try
        {
            await transcriptActivityDetectedAsync(transcriptActivityDetectedContext);
        }
        catch
        {
        }
    }

    private static async Task NotifyTranscriptTurnAbortedAsync(
        CodexTranscriptTurnAbortedContext transcriptTurnAbortedContext,
        Func<CodexTranscriptTurnAbortedContext, Task> transcriptTurnAbortedAsync)
    {
        try
        {
            await transcriptTurnAbortedAsync(transcriptTurnAbortedContext);
        }
        catch
        {
        }
    }

    private void ResetSessionObservationBaseline(LidGuardSessionKey sessionKey)
    {
        lock (_gate)
        {
            if (!_monitoredCodexSessions.TryGetValue(sessionKey, out var monitoredCodexSessionState)) return;

            var currentTranscriptObservation = GetCurrentTranscriptObservation(monitoredCodexSessionState.TranscriptPath);
            monitoredCodexSessionState.LastObservedTranscriptLength = currentTranscriptObservation.Length;
            monitoredCodexSessionState.LastObservedTranscriptLastWriteTimeUtc = currentTranscriptObservation.LastWriteTimeUtc;
        }
    }

    private void RemoveSessionInsideGate(LidGuardSessionKey sessionKey)
    {
        if (!_monitoredCodexSessions.Remove(sessionKey, out var monitoredCodexSessionState)) return;
        monitoredCodexSessionState.FileSystemWatcher.Dispose();
        monitoredCodexSessionState.PollingTimer.Dispose();
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

    private static CodexTranscriptObservation GetCurrentTranscriptObservation(string transcriptPath)
    {
        try
        {
            var transcriptFileInfo = new FileInfo(transcriptPath);
            transcriptFileInfo.Refresh();
            return transcriptFileInfo.Exists
                ? new CodexTranscriptObservation(transcriptFileInfo.Length, transcriptFileInfo.LastWriteTimeUtc)
                : new CodexTranscriptObservation(0, DateTime.MinValue);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException)
        {
            return new CodexTranscriptObservation(0, DateTime.MinValue);
        }
    }

    private static bool IsLastTranscriptLineTurnAborted(string transcriptPath)
    {
        var lastTranscriptLine = ReadLastTranscriptLine(transcriptPath);
        if (string.IsNullOrWhiteSpace(lastTranscriptLine)) return false;

        try
        {
            using var document = JsonDocument.Parse(lastTranscriptLine);
            var rootElement = document.RootElement;
            if (!TryGetStringProperty(rootElement, "type", out var recordType)) return false;
            if (!recordType.Equals("event_msg", StringComparison.Ordinal)) return false;
            if (!rootElement.TryGetProperty("payload", out var payloadElement)) return false;
            if (!TryGetStringProperty(payloadElement, "type", out var payloadType)) return false;
            return payloadType.Equals("turn_aborted", StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private static string ReadLastTranscriptLine(string transcriptPath)
    {
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lastTranscriptLine = string.Empty;
            while (reader.ReadLine() is { } transcriptLine) lastTranscriptLine = transcriptLine;
            return lastTranscriptLine;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var propertyElement)) return false;
        if (propertyElement.ValueKind != JsonValueKind.String) return false;

        value = propertyElement.GetString() ?? string.Empty;
        return true;
    }

    private sealed class MonitoredCodexSessionState
    {
        public required LidGuardSessionKey SessionKey { get; init; }

        public required string WorkingDirectory { get; init; }

        public required string TranscriptPath { get; init; }

        public required FileSystemWatcher FileSystemWatcher { get; init; }

        public required Timer PollingTimer { get; init; }

        public long LastObservedTranscriptLength { get; set; }

        public DateTime LastObservedTranscriptLastWriteTimeUtc { get; set; }

        public bool IsTurnAbortedObserved { get; set; }
    }

    private readonly record struct CodexTranscriptObservation(long Length, DateTime LastWriteTimeUtc);
}

internal sealed class CodexTranscriptMonitoringRegistrationResult
{
    public string ResolvedTranscriptPath { get; init; } = string.Empty;

    public bool MonitoringEnabled { get; init; }

    public string Message { get; init; } = string.Empty;
}

internal sealed class CodexTranscriptActivityDetectedContext
{
    public required LidGuardSessionKey SessionKey { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string TranscriptPath { get; init; }
}

internal sealed class CodexTranscriptTurnAbortedContext
{
    public required LidGuardSessionKey SessionKey { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string TranscriptPath { get; init; }
}

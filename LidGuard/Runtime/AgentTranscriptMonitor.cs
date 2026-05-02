using System.Text.Json;
using LidGuardLib.Commons.Sessions;

namespace LidGuard.Runtime;

internal sealed class AgentTranscriptMonitor(
    AgentTranscriptMonitoringProfile monitoringProfile,
    Func<AgentTranscriptActivityDetectedContext, Task> transcriptActivityDetectedAsync,
    Func<AgentTranscriptStopDetectedContext, Task> transcriptStopDetectedAsync)
{
    private const int TranscriptPollingIntervalMilliseconds = 1000;
    private readonly object _gate = new();
    private readonly Dictionary<LidGuardSessionKey, MonitoredAgentTranscriptSessionState> _monitoredSessions = [];

    public AgentTranscriptMonitoringRegistrationResult RegisterOrUpdateSession(
        string sessionIdentifier,
        string providerName,
        string workingDirectory,
        string transcriptPath)
    {
        var sessionKey = new LidGuardSessionKey(monitoringProfile.Provider, sessionIdentifier, providerName);
        var resolvedTranscriptPath = ResolveTranscriptPath(sessionIdentifier, transcriptPath, out var resolutionMessage);

        lock (_gate)
        {
            RemoveSessionInsideGate(sessionKey);
            if (string.IsNullOrWhiteSpace(resolvedTranscriptPath))
            {
                return new AgentTranscriptMonitoringRegistrationResult
                {
                    Message = resolutionMessage
                };
            }

            var transcriptDirectoryPath = Path.GetDirectoryName(resolvedTranscriptPath);
            var transcriptFileName = Path.GetFileName(resolvedTranscriptPath);
            if (string.IsNullOrWhiteSpace(transcriptDirectoryPath) || string.IsNullOrWhiteSpace(transcriptFileName))
            {
                return new AgentTranscriptMonitoringRegistrationResult
                {
                    ResolvedTranscriptPath = resolvedTranscriptPath,
                    Message = $"Skipped {monitoringProfile.DisplayName} transcript monitoring because '{resolvedTranscriptPath}' is not a valid file path."
                };
            }

            if (!Directory.Exists(transcriptDirectoryPath))
            {
                return new AgentTranscriptMonitoringRegistrationResult
                {
                    ResolvedTranscriptPath = resolvedTranscriptPath,
                    Message = $"Skipped {monitoringProfile.DisplayName} transcript monitoring because transcript directory '{transcriptDirectoryPath}' does not exist."
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
            _monitoredSessions[sessionKey] = new MonitoredAgentTranscriptSessionState
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

        return new AgentTranscriptMonitoringRegistrationResult
        {
            ResolvedTranscriptPath = resolvedTranscriptPath,
            MonitoringEnabled = true,
            Message = $"Watching {monitoringProfile.DisplayName} transcript file '{resolvedTranscriptPath}'. {resolutionMessage}".Trim()
        };
    }

    public void ResetSessionObservationBaseline(LidGuardSessionKey sessionKey)
    {
        lock (_gate)
        {
            if (!_monitoredSessions.TryGetValue(sessionKey, out var monitoredSessionState)) return;

            var currentTranscriptObservation = GetCurrentTranscriptObservation(monitoredSessionState.TranscriptPath);
            monitoredSessionState.LastObservedTranscriptLength = currentTranscriptObservation.Length;
            monitoredSessionState.LastObservedTranscriptLastWriteTimeUtc = currentTranscriptObservation.LastWriteTimeUtc;
        }
    }

    public void RemoveSession(LidGuardSessionKey sessionKey)
    {
        lock (_gate) RemoveSessionInsideGate(sessionKey);
    }

    private void HandleTranscriptFileChanged(LidGuardSessionKey sessionKey)
    {
        AgentTranscriptActivityDetectedContext transcriptActivityDetectedContext = null;
        AgentTranscriptStopDetectedContext transcriptStopDetectedContext = null;

        lock (_gate)
        {
            if (!_monitoredSessions.TryGetValue(sessionKey, out var monitoredSessionState)) return;
            if (monitoredSessionState.IsStopSignalObserved) return;

            var currentTranscriptObservation = GetCurrentTranscriptObservation(monitoredSessionState.TranscriptPath);
            var transcriptLengthIncreased = currentTranscriptObservation.Length > monitoredSessionState.LastObservedTranscriptLength;
            var transcriptLastWriteTimeAdvanced = currentTranscriptObservation.LastWriteTimeUtc > monitoredSessionState.LastObservedTranscriptLastWriteTimeUtc;
            if (!transcriptLengthIncreased && !transcriptLastWriteTimeAdvanced)
            {
                if (currentTranscriptObservation.Length < monitoredSessionState.LastObservedTranscriptLength)
                {
                    monitoredSessionState.LastObservedTranscriptLength = currentTranscriptObservation.Length;
                    monitoredSessionState.LastObservedTranscriptLastWriteTimeUtc = currentTranscriptObservation.LastWriteTimeUtc;
                }

                return;
            }

            monitoredSessionState.LastObservedTranscriptLength = currentTranscriptObservation.Length;
            monitoredSessionState.LastObservedTranscriptLastWriteTimeUtc = currentTranscriptObservation.LastWriteTimeUtc;

            if (monitoringProfile.StopDetector(monitoredSessionState.TranscriptPath))
            {
                monitoredSessionState.IsStopSignalObserved = true;
                transcriptStopDetectedContext = new AgentTranscriptStopDetectedContext
                {
                    SessionKey = monitoredSessionState.SessionKey,
                    WorkingDirectory = monitoredSessionState.WorkingDirectory,
                    TranscriptPath = monitoredSessionState.TranscriptPath,
                    StopCommandName = monitoringProfile.StopCommandName,
                    StopReasonDescription = monitoringProfile.StopReasonDescription
                };
            }
            else
            {
                transcriptActivityDetectedContext = new AgentTranscriptActivityDetectedContext
                {
                    SessionKey = monitoredSessionState.SessionKey,
                    WorkingDirectory = monitoredSessionState.WorkingDirectory,
                    TranscriptPath = monitoredSessionState.TranscriptPath,
                    ActivityReason = monitoringProfile.ActivityReason
                };
            }
        }

        if (transcriptStopDetectedContext is not null)
        {
            _ = NotifyTranscriptStopDetectedAsync(transcriptStopDetectedContext, transcriptStopDetectedAsync);
            return;
        }

        if (transcriptActivityDetectedContext is null) return;
        _ = NotifyTranscriptActivityDetectedAsync(transcriptActivityDetectedContext, transcriptActivityDetectedAsync);
    }

    private static async Task NotifyTranscriptActivityDetectedAsync(
        AgentTranscriptActivityDetectedContext transcriptActivityDetectedContext,
        Func<AgentTranscriptActivityDetectedContext, Task> transcriptActivityDetectedAsync)
    {
        try
        {
            await transcriptActivityDetectedAsync(transcriptActivityDetectedContext);
        }
        catch
        {
        }
    }

    private static async Task NotifyTranscriptStopDetectedAsync(
        AgentTranscriptStopDetectedContext transcriptStopDetectedContext,
        Func<AgentTranscriptStopDetectedContext, Task> transcriptStopDetectedAsync)
    {
        try
        {
            await transcriptStopDetectedAsync(transcriptStopDetectedContext);
        }
        catch
        {
        }
    }

    private void RemoveSessionInsideGate(LidGuardSessionKey sessionKey)
    {
        if (!_monitoredSessions.Remove(sessionKey, out var monitoredSessionState)) return;
        monitoredSessionState.FileSystemWatcher.Dispose();
        monitoredSessionState.PollingTimer.Dispose();
    }

    private string ResolveTranscriptPath(string sessionIdentifier, string transcriptPath, out string resolutionMessage)
    {
        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            var normalizedTranscriptPath = NormalizePath(transcriptPath);
            resolutionMessage = $"Using hook transcript_path '{normalizedTranscriptPath}'.";
            return normalizedTranscriptPath;
        }

        var fallbackRootPath = monitoringProfile.FallbackRootPathResolver();
        var fallbackTranscriptPath = monitoringProfile.FallbackTranscriptPathResolver(sessionIdentifier);
        if (!string.IsNullOrWhiteSpace(fallbackTranscriptPath))
        {
            var normalizedFallbackTranscriptPath = NormalizePath(fallbackTranscriptPath);
            resolutionMessage = $"Resolved transcript path by session-id fallback to '{normalizedFallbackTranscriptPath}'.";
            return normalizedFallbackTranscriptPath;
        }

        if (string.IsNullOrWhiteSpace(fallbackRootPath) || !Directory.Exists(fallbackRootPath))
        {
            resolutionMessage = $"Skipped {monitoringProfile.DisplayName} transcript monitoring because the {monitoringProfile.FallbackRootDescription} directory could not be found.";
            return string.Empty;
        }

        try
        {
            var matchingTranscriptPaths = Directory
                .EnumerateFiles(fallbackRootPath, "*.jsonl", SearchOption.AllDirectories)
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
                ? $"Skipped {monitoringProfile.DisplayName} transcript monitoring because hook input did not include transcript_path and no matching transcript file was found for session '{sessionIdentifier}'."
                : $"Skipped {monitoringProfile.DisplayName} transcript monitoring because hook input did not include transcript_path and multiple matching transcript files were found for session '{sessionIdentifier}'.";
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
        {
            resolutionMessage = $"Skipped {monitoringProfile.DisplayName} transcript monitoring because transcript lookup failed: {exception.Message}";
            return string.Empty;
        }
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path.Trim()); }
        catch { return path.Trim(); }
    }

    private static AgentTranscriptObservation GetCurrentTranscriptObservation(string transcriptPath)
    {
        try
        {
            var transcriptFileInfo = new FileInfo(transcriptPath);
            transcriptFileInfo.Refresh();
            return transcriptFileInfo.Exists
                ? new AgentTranscriptObservation(transcriptFileInfo.Length, transcriptFileInfo.LastWriteTimeUtc)
                : new AgentTranscriptObservation(0, DateTime.MinValue);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException)
        {
            return new AgentTranscriptObservation(0, DateTime.MinValue);
        }
    }

    private sealed class MonitoredAgentTranscriptSessionState
    {
        public required LidGuardSessionKey SessionKey { get; init; }

        public required string WorkingDirectory { get; init; }

        public required string TranscriptPath { get; init; }

        public required FileSystemWatcher FileSystemWatcher { get; init; }

        public required Timer PollingTimer { get; init; }

        public long LastObservedTranscriptLength { get; set; }

        public DateTime LastObservedTranscriptLastWriteTimeUtc { get; set; }

        public bool IsStopSignalObserved { get; set; }
    }

    private readonly record struct AgentTranscriptObservation(long Length, DateTime LastWriteTimeUtc);
}

internal sealed class AgentTranscriptMonitoringProfile
{
    public required AgentProvider Provider { get; init; }

    public required string DisplayName { get; init; }

    public required string FallbackRootDescription { get; init; }

    public required Func<string> FallbackRootPathResolver { get; init; }

    public Func<string, string> FallbackTranscriptPathResolver { get; init; } = static _ => string.Empty;

    public required Func<string, bool> StopDetector { get; init; }

    public required string ActivityReason { get; init; }

    public required string StopCommandName { get; init; }

    public required string StopReasonDescription { get; init; }
}

internal sealed class AgentTranscriptMonitoringRegistrationResult
{
    public string ResolvedTranscriptPath { get; init; } = string.Empty;

    public bool MonitoringEnabled { get; init; }

    public string Message { get; init; } = string.Empty;
}

internal sealed class AgentTranscriptActivityDetectedContext
{
    public required LidGuardSessionKey SessionKey { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string TranscriptPath { get; init; }

    public required string ActivityReason { get; init; }
}

internal sealed class AgentTranscriptStopDetectedContext
{
    public required LidGuardSessionKey SessionKey { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string TranscriptPath { get; init; }

    public required string StopCommandName { get; init; }

    public required string StopReasonDescription { get; init; }
}

internal static class AgentTranscriptStopDetectors
{
    public static bool IsLastCodexTranscriptLineTurnAborted(string transcriptPath)
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

    public static bool IsLastClaudeTranscriptLineInterrupted(string transcriptPath)
    {
        var lastTranscriptLine = ReadLastTranscriptLine(transcriptPath);
        if (string.IsNullOrWhiteSpace(lastTranscriptLine)) return false;

        try
        {
            using var document = JsonDocument.Parse(lastTranscriptLine);
            var rootElement = document.RootElement;
            if (!TryGetStringProperty(rootElement, "type", out var recordType)) return false;
            if (!recordType.Equals("user", StringComparison.Ordinal)) return false;

            if (rootElement.TryGetProperty("message", out var messageElement)
                && messageElement.TryGetProperty("content", out var messageContentElement)
                && ContainsClaudeInterruptMarker(messageContentElement))
            {
                return true;
            }

            if (rootElement.TryGetProperty("content", out var contentElement) && ContainsClaudeInterruptMarker(contentElement)) return true;
            if (TryGetStringProperty(rootElement, "text", out var text)) return IsClaudeInterruptMarker(text);
            return false;
        }
        catch (JsonException) { return false; }
    }

    public static bool IsLastGitHubCopilotSessionEventAbort(string transcriptPath)
    {
        var lastTranscriptLine = ReadLastTranscriptLine(transcriptPath);
        if (string.IsNullOrWhiteSpace(lastTranscriptLine)) return false;

        try
        {
            using var document = JsonDocument.Parse(lastTranscriptLine);
            var rootElement = document.RootElement;
            if (!TryGetStringProperty(rootElement, "type", out var eventType)) return false;
            return eventType.Equals("abort", StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private static bool ContainsClaudeInterruptMarker(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return IsClaudeInterruptMarker(element.GetString() ?? string.Empty);
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var itemElement in element.EnumerateArray())
            {
                if (ContainsClaudeInterruptMarker(itemElement)) return true;
            }

            return false;
        }

        if (element.ValueKind != JsonValueKind.Object) return false;
        if (TryGetStringProperty(element, "text", out var text) && IsClaudeInterruptMarker(text)) return true;
        if (element.TryGetProperty("content", out var contentElement)) return ContainsClaudeInterruptMarker(contentElement);
        return false;
    }

    private static bool IsClaudeInterruptMarker(string text)
    {
        var normalizedText = text.Trim();
        return normalizedText.Equals("[Request interrupted by user]", StringComparison.Ordinal)
            || normalizedText.Equals("[Request interrupted by user for tool use]", StringComparison.Ordinal);
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
}

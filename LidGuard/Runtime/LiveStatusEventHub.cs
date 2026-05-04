using LidGuard.Hooks;

namespace LidGuard.Runtime;

internal sealed class LiveStatusEventHub : IDisposable
{
    private static readonly TimeSpan s_coalesceDelay = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private readonly List<LiveStatusSubscription> _subscriptions = [];
    private readonly List<FileSystemWatcher> _hookLogWatchers = [];
    private bool _disposed;

    public LiveStatusEventHub()
    {
        LidGuardRuntimeSessionLogStore.Appended += Signal;
        SuspendHistoryLogStore.Appended += Signal;
        AddHookLogWatcher(CodexHookEventLog.GetDefaultLogFilePath());
        AddHookLogWatcher(ClaudeHookEventLog.GetDefaultLogFilePath());
        AddHookLogWatcher(GitHubCopilotHookEventLog.GetDefaultLogFilePath());
    }

    public LiveStatusSubscription Subscribe()
    {
        var subscription = new LiveStatusSubscription(this, s_coalesceDelay);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    public void Signal()
    {
        LiveStatusSubscription[] subscriptions;
        lock (_gate)
        {
            if (_disposed) return;
            subscriptions = [.. _subscriptions];
        }

        foreach (var subscription in subscriptions) subscription.Signal();
    }

    public void Dispose()
    {
        LiveStatusSubscription[] subscriptions;
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
            foreach (var hookLogWatcher in _hookLogWatchers) hookLogWatcher.Dispose();
            _hookLogWatchers.Clear();
        }

        LidGuardRuntimeSessionLogStore.Appended -= Signal;
        SuspendHistoryLogStore.Appended -= Signal;
        foreach (var subscription in subscriptions) subscription.Dispose();
    }

    private void Remove(LiveStatusSubscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private void AddHookLogWatcher(string hookLogFilePath)
    {
        try
        {
            var hookLogDirectoryPath = Path.GetDirectoryName(hookLogFilePath);
            var hookLogFileName = Path.GetFileName(hookLogFilePath);
            if (string.IsNullOrWhiteSpace(hookLogDirectoryPath)) return;
            if (string.IsNullOrWhiteSpace(hookLogFileName)) return;

            Directory.CreateDirectory(hookLogDirectoryPath);
            var watcher = new FileSystemWatcher(hookLogDirectoryPath, hookLogFileName)
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += HandleHookLogChanged;
            watcher.Created += HandleHookLogChanged;
            watcher.Deleted += HandleHookLogChanged;
            watcher.Renamed += HandleHookLogRenamed;
            watcher.EnableRaisingEvents = true;
            _hookLogWatchers.Add(watcher);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void HandleHookLogChanged(object sender, FileSystemEventArgs eventArguments) => Signal();

    private void HandleHookLogRenamed(object sender, RenamedEventArgs eventArguments) => Signal();

    internal sealed class LiveStatusSubscription(LiveStatusEventHub owner, TimeSpan coalesceDelay) : IDisposable
    {
        private readonly SemaphoreSlim _signal = new(0);
        private int _hasPendingSignal;
        private int _disposed;

        public async Task WaitForChangeAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            Interlocked.Exchange(ref _hasPendingSignal, 0);
            if (coalesceDelay > TimeSpan.Zero) await Task.Delay(coalesceDelay, cancellationToken);
            DrainCoalescedSignals();
        }

        public void Signal()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (Interlocked.Exchange(ref _hasPendingSignal, 1) != 0) return;

            try { _signal.Release(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            owner.Remove(this);
            _signal.Dispose();
        }

        private void DrainCoalescedSignals()
        {
            while (_signal.Wait(0)) Interlocked.Exchange(ref _hasPendingSignal, 0);
        }
    }
}

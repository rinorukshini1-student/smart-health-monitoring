using System.Collections.Concurrent;

// Thread-safe collector for streaming performance metrics surfaced on the System Health page.
public sealed class MetricsCollector
{
    private long _messagesProcessed;
    private long _windowsComputed;
    private long _alertsGenerated;
    private int _lastBatchSize;

    private readonly ConcurrentQueue<DateTimeOffset> _recentMessages = new();
    private readonly ConcurrentQueue<double> _recentBatchMs = new();

    public void RecordMessage()
    {
        Interlocked.Increment(ref _messagesProcessed);
        _recentMessages.Enqueue(DateTimeOffset.UtcNow);
        Trim();
    }

    public void RecordWindow() => Interlocked.Increment(ref _windowsComputed);

    public void RecordAlert() => Interlocked.Increment(ref _alertsGenerated);

    public void RecordBatch(int size, double elapsedMs)
    {
        _lastBatchSize = size;
        _recentBatchMs.Enqueue(elapsedMs);
        while (_recentBatchMs.Count > 50 && _recentBatchMs.TryDequeue(out _)) { }
    }

    public StreamMetrics Snapshot()
    {
        Trim();
        var perSecond = _recentMessages.Count / 10.0; // messages over the trailing 10s window
        var avgMs = _recentBatchMs.IsEmpty ? 0 : Math.Round(_recentBatchMs.Average(), 2);

        return new StreamMetrics(
            Interlocked.Read(ref _messagesProcessed),
            Math.Round(perSecond, 2),
            _lastBatchSize,
            avgMs,
            Interlocked.Read(ref _windowsComputed),
            Interlocked.Read(ref _alertsGenerated),
            DateTimeOffset.UtcNow);
    }

    private void Trim()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-10);
        while (_recentMessages.TryPeek(out var ts) && ts < cutoff)
        {
            _recentMessages.TryDequeue(out _);
        }
    }
}

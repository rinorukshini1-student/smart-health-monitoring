using System.Collections.Concurrent;

namespace Vue.Api.Services;

public sealed class SystemMetricsService
{
    private readonly ConcurrentQueue<DateTimeOffset> _recentMessages = new();
    private readonly ConcurrentQueue<double> _apiResponseTimes = new();
    private long _messagesReceived;
    private long _alertsReceived;
    private volatile StreamMetricsDto? _lastStreamMetrics;
    private DateTimeOffset _lastStreamMetricsAt = DateTimeOffset.MinValue;

    public void RecordVital()
    {
        Interlocked.Increment(ref _messagesReceived);
        _recentMessages.Enqueue(DateTimeOffset.UtcNow);
        TrimMessages();
    }

    public void RecordAlert() => Interlocked.Increment(ref _alertsReceived);

    public void RecordStreamMetrics(StreamMetricsDto metrics)
    {
        _lastStreamMetrics = metrics;
        _lastStreamMetricsAt = DateTimeOffset.UtcNow;
    }

    public void RecordApiResponse(double milliseconds)
    {
        _apiResponseTimes.Enqueue(milliseconds);
        while (_apiResponseTimes.Count > 100 && _apiResponseTimes.TryDequeue(out _)) { }
    }

    public double MessagesPerSecond()
    {
        TrimMessages();
        return Math.Round(_recentMessages.Count / 10.0, 2);
    }

    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);

    public long AlertsReceived => Interlocked.Read(ref _alertsReceived);

    public double ApiAverageResponseMs => _apiResponseTimes.IsEmpty ? 0 : Math.Round(_apiResponseTimes.Average(), 2);

    public StreamMetricsDto? LastStreamMetrics => _lastStreamMetrics;

    public bool StreamingOnline => (DateTimeOffset.UtcNow - _lastStreamMetricsAt) < TimeSpan.FromSeconds(15);

    private void TrimMessages()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-10);
        while (_recentMessages.TryPeek(out var ts) && ts < cutoff)
        {
            _recentMessages.TryDequeue(out _);
        }
    }
}

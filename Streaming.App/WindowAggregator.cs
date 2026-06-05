using System.Collections.Concurrent;

// In-memory 5-minute sliding window aggregator used by the direct Kafka mode.
// Mirrors the semantics of the Spark structured-streaming window aggregation so the
// analytics dashboard works the same way whether the processor runs in Spark or direct mode.
public sealed class WindowAggregator
{
    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, List<VitalReading>> _buffers = new();

    // Adds a reading and returns the recomputed aggregate for the patient's trailing 5-minute window.
    public WindowAggregate Add(VitalReading reading)
    {
        var buffer = _buffers.GetOrAdd(reading.PatientId, _ => new List<VitalReading>());

        lock (buffer)
        {
            buffer.Add(reading);
            var cutoff = reading.RecordedAt - WindowSize;
            buffer.RemoveAll(r => r.RecordedAt < cutoff);

            var windowStart = FloorToMinute(reading.RecordedAt);
            return new WindowAggregate(
                reading.PatientId,
                reading.RoomNumber,
                windowStart,
                Math.Round(buffer.Average(r => r.HeartRate), 1),
                Math.Round(buffer.Average(r => r.Spo2), 1),
                Math.Round(buffer.Average(r => r.Temperature), 2),
                Math.Round(buffer.Average(r => r.SystolicBp), 1),
                Math.Round(buffer.Average(r => r.DiastolicBp), 1),
                Math.Round(buffer.Average(r => r.RespiratoryRate), 1),
                buffer.Count);
        }
    }

    private static DateTimeOffset FloorToMinute(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}

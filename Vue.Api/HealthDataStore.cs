using System.Collections.Concurrent;

namespace Vue.Api;

// In-memory store backing the self-contained Vue dashboard.
// Holds rolling buffers of readings/alerts/risk/windows/ML predictions and answers all dashboard queries,
// so the dashboard works the instant the API starts - no Kafka/Cassandra required.
public sealed class HealthDataStore
{
    private readonly object _gate = new();
    private readonly List<VitalReadingDto> _readings = new();
    private readonly List<AlertMessageDto> _alerts = new();
    private readonly List<WindowAggregate> _windows = new();
    private readonly List<RiskScoreDto> _riskHistory = new();
    private readonly Dictionary<string, List<VitalReadingDto>> _windowBuffers = new();

    private readonly ConcurrentDictionary<string, RiskScoreDto> _latestRisk = new();
    private readonly ConcurrentDictionary<string, MlPredictionDto> _latestMl = new();
    private readonly List<MlPredictionDto> _mlHistory = new();

    private readonly ConcurrentQueue<DateTimeOffset> _recentMessages = new();
    private readonly ConcurrentQueue<double> _apiTimes = new();
    private long _messages, _alertsGenerated, _windowsComputed;
    private int _lastBatchSize;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<PatientDefinition> Patients { get; }
    private readonly Dictionary<string, PatientDefinition> _patientById;

    public HealthDataStore(IReadOnlyList<PatientDefinition> patients)
    {
        Patients = patients;
        _patientById = patients.ToDictionary(p => p.PatientId);
    }

    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(5);

    // ---------------- Writes (used by the generator) ----------------

    public WindowAggregate AddReading(VitalReadingDto r)
    {
        WindowAggregate agg;
        lock (_gate)
        {
            _readings.Add(r);

            if (!_windowBuffers.TryGetValue(r.PatientId, out var buffer))
            {
                buffer = new List<VitalReadingDto>();
                _windowBuffers[r.PatientId] = buffer;
            }
            buffer.Add(r);
            var cutoff = r.RecordedAt - WindowSize;
            buffer.RemoveAll(x => x.RecordedAt < cutoff);

            agg = new WindowAggregate(
                r.PatientId, r.RoomNumber, FloorMinute(r.RecordedAt),
                Math.Round(buffer.Average(x => x.HeartRate), 1),
                Math.Round(buffer.Average(x => x.Spo2), 1),
                Math.Round(buffer.Average(x => x.Temperature), 2),
                Math.Round(buffer.Average(x => x.SystolicBp), 1),
                Math.Round(buffer.Average(x => x.DiastolicBp), 1),
                Math.Round(buffer.Average(x => x.RespiratoryRate), 1),
                buffer.Count);
            _windows.Add(agg);

            TrimLocked();
        }

        Interlocked.Increment(ref _messages);
        Interlocked.Increment(ref _windowsComputed);
        _recentMessages.Enqueue(DateTimeOffset.UtcNow);
        TrimRecent();
        return agg;
    }

    public void RecordBatch(int size) => _lastBatchSize = size;

    public void AddAlert(AlertMessageDto a)
    {
        lock (_gate) { _alerts.Add(a); }
        Interlocked.Increment(ref _alertsGenerated);
    }

    public void AddRisk(RiskScoreDto r)
    {
        _latestRisk[r.PatientId] = r;
        lock (_gate) { _riskHistory.Add(r); }
    }

    public void AddMlPrediction(MlPredictionDto p)
    {
        _latestMl[p.PatientId] = p;
        lock (_gate) { _mlHistory.Add(p); }
    }

    public void RecordApiResponse(double ms)
    {
        _apiTimes.Enqueue(ms);
        while (_apiTimes.Count > 100 && _apiTimes.TryDequeue(out _)) { }
    }

    // ---------------- Reads ----------------

    public IReadOnlyList<PatientProfileDto> GetProfiles() => Patients.Select(p => p.ToProfile()).ToArray();

    public PatientProfileDto? GetProfile(string id) => _patientById.TryGetValue(id, out var p) ? p.ToProfile() : null;

    public IReadOnlyList<LivePatientRow> GetLiveSnapshot()
    {
        var latest = LatestPerPatient();
        return latest.Values.OrderBy(v => v.RoomNumber).Select(v =>
        {
            _latestRisk.TryGetValue(v.PatientId, out var risk);
            var age = _patientById.TryGetValue(v.PatientId, out var def) ? def.Age : v.Age;
            return new LivePatientRow(
                v.PatientId, v.PatientName, v.RoomNumber, age,
                v.HeartRate, v.Temperature, v.Spo2, v.SystolicBp, v.DiastolicBp, v.RespiratoryRate,
                risk?.Score ?? 0, risk?.Category ?? RiskScoringEngine.LowRisk, AlertRules.Classify(v), v.RecordedAt);
        }).ToArray();
    }

    public OverviewDto GetOverview()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = GetLiveSnapshot();
        var active = snapshot.Where(s => (now - s.LastUpdate) < TimeSpan.FromMinutes(2)).ToArray();

        List<VitalReadingDto> recent;
        List<AlertMessageDto> recentAlerts;
        lock (_gate)
        {
            var since = now.AddMinutes(-30);
            recent = _readings.Where(r => r.RecordedAt >= since).ToList();
            recentAlerts = _alerts.ToList();
        }

        var messagesPerMinute = recent.GroupBy(v => FloorMinute(v.RecordedAt)).OrderBy(g => g.Key).TakeLast(30)
            .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), g.Count())).ToArray();
        var hrTrend = recent.GroupBy(v => FloorMinute(v.RecordedAt)).OrderBy(g => g.Key).TakeLast(30)
            .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Average(x => x.HeartRate), 1))).ToArray();
        var tempTrend = recent.GroupBy(v => FloorMinute(v.RecordedAt)).OrderBy(g => g.Key).TakeLast(30)
            .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Average(x => x.Temperature), 2))).ToArray();
        var alertsPerHour = recentAlerts.GroupBy(a => FloorHour(a.RecordedAt)).OrderBy(g => g.Key)
            .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:00"), g.Count())).ToArray();

        var recentReadingEvents = recent.OrderByDescending(v => v.RecordedAt).Take(10)
            .Select(v => new RecentEvent("reading", v.PatientId, v.RoomNumber, $"Pulsi {v.HeartRate} · SpO₂ {v.Spo2}% · {v.Temperature:0.0}°C", "INFO", v.RecordedAt)).ToArray();
        var recentAlertEvents = recentAlerts.OrderByDescending(a => a.RecordedAt).Take(10)
            .Select(a => new RecentEvent("alert", a.PatientId, a.RoomNumber, a.Message, a.Severity, a.RecordedAt)).ToArray();

        return new OverviewDto(
            snapshot.Count, active.Length,
            recentAlerts.Count(a => a.Severity == "CRITICAL"),
            active.Length == 0 ? 0 : Math.Round(active.Average(s => s.HeartRate), 0),
            active.Length == 0 ? 0 : Math.Round(active.Average(s => s.Temperature), 1),
            active.Length == 0 ? 0 : Math.Round(active.Average(s => s.Spo2), 0),
            snapshot.Count(s => s.RiskCategory == RiskScoringEngine.HighRisk),
            messagesPerMinute, alertsPerHour, hrTrend, tempTrend, recentReadingEvents, recentAlertEvents);
    }

    public PatientDetailDto? GetPatientDetail(string id, string range)
    {
        if (!_patientById.TryGetValue(id, out var def)) return null;

        var window = range switch { "week" => TimeSpan.FromDays(7), "hour" => TimeSpan.FromHours(1), _ => TimeSpan.FromHours(24) };
        var since = DateTimeOffset.UtcNow - window;

        List<VitalReadingDto> samples;
        List<RiskScoreDto> risk;
        lock (_gate)
        {
            samples = _readings.Where(r => r.PatientId == id && r.RecordedAt >= since).OrderBy(r => r.RecordedAt).ToList();
            risk = _riskHistory.Where(r => r.PatientId == id && r.RecordedAt >= since).OrderBy(r => r.RecordedAt).ToList();
        }

        var ordered = Downsample(samples, 240);
        var current = risk.Count > 0 ? risk[^1] : null;

        return new PatientDetailDto(
            def.PatientId, def.PatientName, def.RoomNumber, def.Age, range,
            ordered.Select(s => s.RecordedAt).ToArray(),
            ordered.Select(s => s.HeartRate).ToArray(),
            ordered.Select(s => s.Temperature).ToArray(),
            ordered.Select(s => s.Spo2).ToArray(),
            ordered.Select(s => s.SystolicBp).ToArray(),
            ordered.Select(s => s.DiastolicBp).ToArray(),
            ordered.Select(s => s.RespiratoryRate).ToArray(),
            Stat(ordered.Select(s => (double)s.HeartRate)),
            Stat(ordered.Select(s => s.Temperature)),
            Stat(ordered.Select(s => (double)s.Spo2)),
            Stat(ordered.Select(s => (double)s.SystolicBp)),
            current?.Score ?? 0, current?.Category ?? RiskScoringEngine.LowRisk, current?.Factors ?? "Ende pa vlerësim",
            risk.Select(r => new RiskPoint(r.RecordedAt, r.Score, r.Category)).ToArray());
    }

    public IReadOnlyList<AlertMessageDto> GetAlerts(string? severity, string? type, string? query, int limit)
    {
        List<AlertMessageDto> all;
        lock (_gate) { all = _alerts.ToList(); }

        IEnumerable<AlertMessageDto> q = all;
        if (!string.IsNullOrWhiteSpace(severity)) q = q.Where(a => a.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(a => a.AlertType.Equals(type, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query)) q = q.Where(a =>
            a.PatientId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            a.RoomNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            a.Message.Contains(query, StringComparison.OrdinalIgnoreCase));

        return q.OrderByDescending(a => a.RecordedAt).Take(limit).ToArray();
    }

    public AnalyticsDto GetAnalytics()
    {
        var since = DateTimeOffset.UtcNow.AddHours(-6);
        List<WindowAggregate> windows;
        List<AlertMessageDto> alerts;
        lock (_gate)
        {
            windows = _windows.Where(w => w.WindowStart >= since).ToList();
            alerts = _alerts.ToList();
        }

        var grouped = windows.GroupBy(w => w.WindowStart).OrderBy(g => g.Key).TakeLast(60).ToArray();
        var avgHr = grouped.Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Average(x => x.AvgHeartRate), 1))).ToArray();
        var avgTemp = grouped.Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Average(x => x.AvgTemperature), 2))).ToArray();
        var avgSpo2 = grouped.Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Average(x => x.AvgSpo2), 1))).ToArray();

        var byType = alerts.GroupBy(a => string.IsNullOrEmpty(a.AlertType) ? "Other" : a.AlertType)
            .Select(g => new TimeSeriesPoint(g.Key, g.Count())).OrderByDescending(p => p.Value).ToArray();

        return new AnalyticsDto(avgHr, avgTemp, avgSpo2, byType, GetHighRisk(8), windows.Count);
    }

    public IReadOnlyList<RiskLeaderRow> GetHighRisk(int limit) =>
        _latestRisk.Values.OrderByDescending(r => r.Score).Take(limit)
            .Select(r => new RiskLeaderRow(r.PatientId, r.RoomNumber, r.Score, r.Category, r.Factors)).ToArray();

    public SystemMetricsDto GetSystemMetrics()
    {
        int vitals, alerts;
        lock (_gate) { vitals = _readings.Count; alerts = _alerts.Count; }
        var mps = MessagesPerSecond();
        return new SystemMetricsDto(
            Interlocked.Read(ref _messages), mps, _lastBatchSize,
            0.5, Interlocked.Read(ref _windowsComputed), vitals, alerts, mps,
            _apiTimes.IsEmpty ? 0 : Math.Round(_apiTimes.Average(), 2),
            Interlocked.Read(ref _alertsGenerated), true, DateTimeOffset.UtcNow);
    }

    public StreamMetricsDto GetStreamMetrics() => new(
        Interlocked.Read(ref _messages), MessagesPerSecond(), _lastBatchSize, 0.5,
        Interlocked.Read(ref _windowsComputed), Interlocked.Read(ref _alertsGenerated), DateTimeOffset.UtcNow);

    public IReadOnlyList<MlPredictionDto> GetMlPredictions() =>
        _latestMl.Values.OrderByDescending(p => p.RiskProbability).ToArray();

    public MlPredictionDto? GetMlPrediction(string id) => _latestMl.TryGetValue(id, out var p) ? p : null;

    public IReadOnlyList<MlPredictionPoint> GetMlHistory(string id)
    {
        lock (_gate)
        {
            return _mlHistory.Where(p => p.PatientId == id).OrderBy(p => p.RecordedAt)
                .Select(p => new MlPredictionPoint(p.RecordedAt, p.RiskProbability, p.RiskCategory)).ToArray();
        }
    }

    // ---------------- Helpers ----------------

    private Dictionary<string, VitalReadingDto> LatestPerPatient()
    {
        lock (_gate)
        {
            var dict = new Dictionary<string, VitalReadingDto>();
            foreach (var r in _readings)
            {
                if (!dict.TryGetValue(r.PatientId, out var e) || r.RecordedAt > e.RecordedAt) dict[r.PatientId] = r;
            }
            return dict;
        }
    }

    private double MessagesPerSecond()
    {
        TrimRecent();
        return Math.Round(_recentMessages.Count / 10.0, 2);
    }

    private void TrimRecent()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-10);
        while (_recentMessages.TryPeek(out var ts) && ts < cutoff) _recentMessages.TryDequeue(out _);
    }

    // Caller holds _gate.
    private void TrimLocked()
    {
        var now = DateTimeOffset.UtcNow;
        var readingCutoff = now.AddHours(-3);
        var alertCutoff = now.AddHours(-24);
        if (_readings.Count > 80000) _readings.RemoveAll(r => r.RecordedAt < readingCutoff);
        if (_windows.Count > 80000) _windows.RemoveAll(w => w.WindowStart < readingCutoff);
        if (_alerts.Count > 20000) _alerts.RemoveAll(a => a.RecordedAt < alertCutoff);
        if (_riskHistory.Count > 80000) _riskHistory.RemoveAll(r => r.RecordedAt < readingCutoff);
        if (_mlHistory.Count > 20000) _mlHistory.RemoveAll(p => p.RecordedAt < readingCutoff);
    }

    private static VitalStat Stat(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return new VitalStat(0, 0, 0);
        return new VitalStat(Math.Round(list.Min(), 1), Math.Round(list.Max(), 1), Math.Round(list.Average(), 1));
    }

    private static List<VitalReadingDto> Downsample(List<VitalReadingDto> source, int max)
    {
        if (source.Count <= max) return source;
        var step = (int)Math.Ceiling(source.Count / (double)max);
        return source.Where((_, i) => i % step == 0).ToList();
    }

    private static DateTimeOffset FloorMinute(DateTimeOffset v)
    {
        var u = v.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, u.Minute, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset FloorHour(DateTimeOffset v)
    {
        var u = v.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, 0, 0, TimeSpan.Zero);
    }
}

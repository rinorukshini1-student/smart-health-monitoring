using Cassandra;
using Microsoft.Extensions.Options;
namespace Vue.Api.Data;

public sealed class CassandraHealthStatsRepository : IHealthStatsRepository, IAsyncDisposable
{
    private static readonly string[] Rooms = Enumerable.Range(101, 10).Select(room => room.ToString()).ToArray();
    private readonly ICluster _cluster;
    private readonly Lazy<Task<Cassandra.ISession>> _session;
    private readonly CassandraOptions _options;
    private readonly ILogger<CassandraHealthStatsRepository> _logger;

    public CassandraHealthStatsRepository(IOptions<CassandraOptions> options, ILogger<CassandraHealthStatsRepository> logger)
    {
        _logger = logger;
        _options = options.Value;
        _cluster = Cluster.Builder()
            .AddContactPoint(_options.ContactPoint)
            .WithPort(_options.Port)
            .WithSocketOptions(new SocketOptions().SetConnectTimeoutMillis(3000))
            .Build();

        _session = new Lazy<Task<Cassandra.ISession>>(InitializeSessionAsync);
    }

    private async Task<Cassandra.ISession> InitializeSessionAsync()
    {
        var systemSession = await _cluster.ConnectAsync();
        await systemSession.ExecuteAsync(new SimpleStatement(
            $"CREATE KEYSPACE IF NOT EXISTS {_options.Keyspace} WITH replication = {{ 'class': 'SimpleStrategy', 'replication_factor': 1 }}"));

        var session = await _cluster.ConnectAsync(_options.Keyspace);

        // The streaming processor owns schema creation/migration; the dashboard only ensures the
        // base tables exist so it can start independently during a demo.
        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS patient_vitals (
              room_number text, vital_day date, recorded_at timestamp, patient_id text, patient_name text,
              age int, heart_rate int, spo2 int, temperature double, systolic_bp int, diastolic_bp int, respiratory_rate int,
              PRIMARY KEY ((room_number, vital_day), recorded_at, patient_id)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC, patient_id ASC)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS alerts_log (
              room_number text, alert_day date, recorded_at timestamp, alert_id uuid, patient_id text,
              alert_type text, severity text, message text, value double,
              heart_rate int, spo2 int, temperature double, systolic_bp int, diastolic_bp int, respiratory_rate int,
              PRIMARY KEY ((room_number, alert_day), recorded_at, alert_id)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC, alert_id ASC)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS patients (
              patient_id text PRIMARY KEY, patient_name text, room_number text, age int,
              sex text, cholesterol int, diabetes int, family_history int, smoking int, obesity int,
              alcohol_consumption int, exercise_hours_per_week double, diet text, previous_heart_problems int,
              medication_use int, stress_level int, sedentary_hours_per_day double, bmi double, triglycerides int,
              physical_activity_days_per_week int, sleep_hours_per_day int)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS ml_predictions (
              patient_id text, prediction_day date, recorded_at timestamp, room_number text,
              risk_probability double, risk_category text, top_factors text,
              PRIMARY KEY ((patient_id, prediction_day), recorded_at)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS ai_risk_scores (
              patient_id text, score_day date, recorded_at timestamp, room_number text, score int, category text, factors text,
              heart_rate int, spo2 int, temperature double, systolic_bp int, diastolic_bp int, respiratory_rate int,
              PRIMARY KEY ((patient_id, score_day), recorded_at)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS vitals_window_agg (
              room_number text, window_day date, window_start timestamp, patient_id text,
              avg_heart_rate double, avg_spo2 double, avg_temperature double, avg_systolic double, avg_diastolic double, avg_respiratory double, sample_count int,
              PRIMARY KEY ((room_number, window_day), window_start, patient_id)
            ) WITH CLUSTERING ORDER BY (window_start DESC, patient_id ASC)
            """));

        await systemSession.ShutdownAsync();
        return session;
    }

    // ---------------- Live snapshot (Page 2) ----------------

    public async Task<IReadOnlyList<LivePatientRow>> GetLiveSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var days = RecentDays(TimeSpan.FromHours(24));
            var latestByPatient = new Dictionary<string, VitalSample>();

            foreach (var room in Rooms)
            {
                foreach (var day in days)
                {
                    var rows = await session.ExecuteAsync(new SimpleStatement(
                        "SELECT * FROM patient_vitals WHERE room_number = ? AND vital_day = ? LIMIT 100", room, day));

                    foreach (var row in rows)
                    {
                        var sample = ReadVital(row);
                        if (!latestByPatient.TryGetValue(sample.PatientId, out var existing) || sample.Timestamp > existing.Timestamp)
                        {
                            latestByPatient[sample.PatientId] = sample;
                        }
                    }
                }
            }

            var risk = await GetLatestRiskByPatientAsync(session, latestByPatient.Keys, days);

            return latestByPatient.Values
                .OrderBy(v => v.RoomNumber)
                .Select(v =>
                {
                    risk.TryGetValue(v.PatientId, out var r);
                    return new LivePatientRow(
                        v.PatientId, v.PatientName, v.RoomNumber, v.Age,
                        v.HeartRate, v.Temperature, v.Spo2, v.SystolicBp, v.DiastolicBp, v.RespiratoryRate,
                        r?.Score ?? 0, r?.Category ?? RiskScoringEngine.LowRisk,
                        Classify(v), v.Timestamp);
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load live snapshot.");
            return Array.Empty<LivePatientRow>();
        }
    }

    // ---------------- Overview (Page 1) ----------------

    public async Task<OverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var snapshot = await GetLiveSnapshotAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;

            var active = snapshot.Where(s => (now - s.LastUpdate) < TimeSpan.FromMinutes(2)).ToArray();
            var avgHr = active.Length == 0 ? 0 : Math.Round(active.Average(s => s.HeartRate), 0);
            var avgTemp = active.Length == 0 ? 0 : Math.Round(active.Average(s => s.Temperature), 1);
            var avgSpo2 = active.Length == 0 ? 0 : Math.Round(active.Average(s => s.Spo2), 0);
            var highRisk = snapshot.Count(s => s.RiskCategory == RiskScoringEngine.HighRisk || s.RiskCategory == "High Risk");

            var recentVitals = await ScanRecentVitalsAsync(session, TimeSpan.FromMinutes(30), 1000);
            var recentAlerts = await ScanRecentAlertsAsync(session, TimeSpan.FromHours(24), 2000);

            var messagesPerMinute = recentVitals
                .GroupBy(v => FloorMinute(v.Timestamp))
                .OrderBy(g => g.Key)
                .TakeLast(30)
                .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), g.Count()))
                .ToArray();

            var heartRateTrend = recentVitals
                .GroupBy(v => FloorMinute(v.Timestamp))
                .OrderBy(g => g.Key)
                .TakeLast(30)
                .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Average(x => x.HeartRate), 1)))
                .ToArray();

            var temperatureTrend = recentVitals
                .GroupBy(v => FloorMinute(v.Timestamp))
                .OrderBy(g => g.Key)
                .TakeLast(30)
                .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Average(x => x.Temperature), 2)))
                .ToArray();

            var alertsPerHour = recentAlerts
                .GroupBy(a => FloorHour(a.Timestamp))
                .OrderBy(g => g.Key)
                .Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:00"), g.Count()))
                .ToArray();

            var recentReadingEvents = recentVitals
                .OrderByDescending(v => v.Timestamp)
                .Take(10)
                .Select(v => new RecentEvent("reading", v.PatientId, v.RoomNumber,
                    $"HR {v.HeartRate} · SpO2 {v.Spo2}% · {v.Temperature:0.0}C", "INFO", v.Timestamp))
                .ToArray();

            var recentAlertEvents = recentAlerts
                .OrderByDescending(a => a.Timestamp)
                .Take(10)
                .Select(a => new RecentEvent("alert", a.PatientId, a.RoomNumber, a.Message, a.Severity, a.Timestamp))
                .ToArray();

            var criticalAlerts = recentAlerts.Count(a => a.Severity == "CRITICAL");

            return new OverviewDto(
                snapshot.Count,
                active.Length,
                criticalAlerts,
                avgHr,
                avgTemp,
                avgSpo2,
                highRisk,
                messagesPerMinute,
                alertsPerHour,
                heartRateTrend,
                temperatureTrend,
                recentReadingEvents,
                recentAlertEvents);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load overview.");
            return new OverviewDto(0, 0, 0, 0, 0, 0, 0,
                Array.Empty<TimeSeriesPoint>(), Array.Empty<TimeSeriesPoint>(),
                Array.Empty<TimeSeriesPoint>(), Array.Empty<TimeSeriesPoint>(),
                Array.Empty<RecentEvent>(), Array.Empty<RecentEvent>());
        }
    }

    // ---------------- Patient detail (Page 3) ----------------

    public async Task<PatientDetailDto?> GetPatientDetailAsync(string patientId, string range, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var window = range switch
            {
                "week" => TimeSpan.FromDays(7),
                "hour" => TimeSpan.FromHours(1),
                _ => TimeSpan.FromHours(24)
            };

            var patient = await GetPatientAsync(session, patientId);
            if (patient is null)
            {
                return null;
            }

            var since = DateTimeOffset.UtcNow - window;
            var days = RecentDays(window);
            var samples = new List<VitalSample>();

            foreach (var day in days)
            {
                var rows = await session.ExecuteAsync(new SimpleStatement(
                    "SELECT * FROM patient_vitals WHERE room_number = ? AND vital_day = ?", patient.RoomNumber, day));

                foreach (var row in rows)
                {
                    var sample = ReadVital(row);
                    if (sample.PatientId == patientId && sample.Timestamp >= since)
                    {
                        samples.Add(sample);
                    }
                }
            }

            var ordered = Downsample(samples.OrderBy(s => s.Timestamp).ToList(), 240);

            var risk = await GetRiskHistoryAsync(session, patientId, since, days);
            var current = risk.Count > 0 ? risk[^1] : null;

            return new PatientDetailDto(
                patient.PatientId, patient.PatientName, patient.RoomNumber, patient.Age, range,
                ordered.Select(s => s.Timestamp).ToArray(),
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
                current?.Score ?? 0,
                current?.Category ?? RiskScoringEngine.LowRisk,
                current?.Factors ?? "Ende pa vlerësim",
                risk.Select(r => new RiskPoint(r.Timestamp, r.Score, r.Category)).ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load patient detail for {PatientId}.", patientId);
            return null;
        }
    }

    // ---------------- Alerts (Page 4) ----------------

    public async Task<IReadOnlyList<AlertMessageDto>> GetAlertsAsync(string? severity, string? type, string? query, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var alerts = await ScanRecentAlertsAsync(session, TimeSpan.FromHours(24), 4000);

            IEnumerable<AlertSample> filtered = alerts;
            if (!string.IsNullOrWhiteSpace(severity))
            {
                filtered = filtered.Where(a => string.Equals(a.Severity, severity, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(type))
            {
                filtered = filtered.Where(a => string.Equals(a.AlertType, type, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(a =>
                    a.PatientId.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || a.RoomNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || a.Message.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return filtered
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .Select(a => new AlertMessageDto(
                    a.AlertId, a.PatientId, a.RoomNumber, a.AlertType, a.Severity, a.Message, a.Value,
                    a.HeartRate, a.Spo2, a.Temperature, a.SystolicBp, a.DiastolicBp, a.RespiratoryRate, a.Timestamp))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load alerts.");
            return Array.Empty<AlertMessageDto>();
        }
    }

    // ---------------- Analytics (Page 5) ----------------

    public async Task<AnalyticsDto> GetAnalyticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var days = RecentDays(TimeSpan.FromHours(24));
            var since = DateTimeOffset.UtcNow.AddHours(-6);

            // Average per Spark window (averaged across patients for each window_start).
            var windowGroups = new Dictionary<DateTimeOffset, (double hr, double temp, double spo2, int n)>();
            var totalWindows = 0;

            foreach (var room in Rooms)
            {
                foreach (var day in days)
                {
                    var rows = await session.ExecuteAsync(new SimpleStatement(
                        "SELECT * FROM vitals_window_agg WHERE room_number = ? AND window_day = ? LIMIT 500", room, day));

                    foreach (var row in rows)
                    {
                        var start = new DateTimeOffset(row.GetValue<DateTime>("window_start"), TimeSpan.Zero);
                        if (start < since) continue;
                        totalWindows++;

                        var key = start;
                        windowGroups.TryGetValue(key, out var agg);
                        windowGroups[key] = (
                            agg.hr + GetDouble(row, "avg_heart_rate"),
                            agg.temp + GetDouble(row, "avg_temperature"),
                            agg.spo2 + GetDouble(row, "avg_spo2"),
                            agg.n + 1);
                    }
                }
            }

            var ordered = windowGroups.OrderBy(g => g.Key).TakeLast(60).ToArray();
            var avgHr = ordered.Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Value.hr / Math.Max(1, g.Value.n), 1))).ToArray();
            var avgTemp = ordered.Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Value.temp / Math.Max(1, g.Value.n), 2))).ToArray();
            var avgSpo2 = ordered.Select(g => new TimeSeriesPoint(g.Key.ToLocalTime().ToString("HH:mm"), Math.Round(g.Value.spo2 / Math.Max(1, g.Value.n), 1))).ToArray();

            var alerts = await ScanRecentAlertsAsync(session, TimeSpan.FromHours(24), 4000);
            var byType = alerts
                .GroupBy(a => string.IsNullOrEmpty(a.AlertType) ? "Other" : a.AlertType)
                .Select(g => new TimeSeriesPoint(g.Key, g.Count()))
                .OrderByDescending(p => p.Value)
                .ToArray();

            var highRisk = await GetHighRiskPatientsAsync(8, cancellationToken);

            return new AnalyticsDto(avgHr, avgTemp, avgSpo2, byType, highRisk, totalWindows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load analytics.");
            return new AnalyticsDto(
                Array.Empty<TimeSeriesPoint>(), Array.Empty<TimeSeriesPoint>(), Array.Empty<TimeSeriesPoint>(),
                Array.Empty<TimeSeriesPoint>(), Array.Empty<RiskLeaderRow>(), 0);
        }
    }

    public async Task<IReadOnlyList<RiskLeaderRow>> GetHighRiskPatientsAsync(int limit, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var days = RecentDays(TimeSpan.FromHours(24));
            var patients = await LoadPatientsAsync(session);
            var latest = await GetLatestRiskByPatientAsync(session, patients.Select(p => p.PatientId), days);

            return latest.Values
                .OrderByDescending(r => r.Score)
                .Take(limit)
                .Select(r => new RiskLeaderRow(r.PatientId, r.RoomNumber, r.Score, r.Category, r.Factors))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load high-risk patients.");
            return Array.Empty<RiskLeaderRow>();
        }
    }

    // ---------------- System health (Page 6) ----------------

    public async Task<(long vitals, long alerts)> GetStoredCountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var today = ToCassandraDate(DateTimeOffset.UtcNow);
            long vitals = 0, alerts = 0;

            foreach (var room in Rooms)
            {
                var v = await session.ExecuteAsync(new SimpleStatement(
                    "SELECT COUNT(*) AS c FROM patient_vitals WHERE room_number = ? AND vital_day = ?", room, today));
                vitals += v.First().GetValue<long>("c");

                var a = await session.ExecuteAsync(new SimpleStatement(
                    "SELECT COUNT(*) AS c FROM alerts_log WHERE room_number = ? AND alert_day = ?", room, today));
                alerts += a.First().GetValue<long>("c");
            }

            return (vitals, alerts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load stored counts.");
            return (0, 0);
        }
    }

    // ---------------- Heart-attack ML module ----------------

    public async Task<IReadOnlyList<PatientProfileDto>> GetPatientProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var rows = await session.ExecuteAsync(new SimpleStatement("SELECT * FROM patients"));
            return rows.Select(ReadProfile).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load patient profiles.");
            return Array.Empty<PatientProfileDto>();
        }
    }

    public async Task<PatientProfileDto?> GetPatientProfileAsync(string patientId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var rows = await session.ExecuteAsync(new SimpleStatement("SELECT * FROM patients WHERE patient_id = ?", patientId));
            var row = rows.FirstOrDefault();
            return row is null ? null : ReadProfile(row);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load patient profile {PatientId}.", patientId);
            return null;
        }
    }

    public async Task StoreMlPredictionAsync(MlPredictionDto p, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            await session.ExecuteAsync(new SimpleStatement(
                """
                INSERT INTO ml_predictions (patient_id, prediction_day, recorded_at, room_number, risk_probability, risk_category, top_factors)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                p.PatientId, ToCassandraDate(p.RecordedAt), p.RecordedAt.UtcDateTime, p.RoomNumber,
                p.RiskProbability, p.RiskCategory, string.Join(" | ", p.TopFactors)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not store ML prediction for {PatientId}.", p.PatientId);
        }
    }

    public async Task StoreAlertAsync(AlertMessageDto a, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            await session.ExecuteAsync(new SimpleStatement(
                """
                INSERT INTO alerts_log (room_number, alert_day, recorded_at, alert_id, patient_id, alert_type, severity, message, value,
                  heart_rate, spo2, temperature, systolic_bp, diastolic_bp, respiratory_rate)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                a.RoomNumber, ToCassandraDate(a.RecordedAt), a.RecordedAt.UtcDateTime, a.AlertId, a.PatientId,
                a.AlertType, a.Severity, a.Message, a.Value,
                a.HeartRate, a.Spo2, a.Temperature, a.SystolicBp, a.DiastolicBp, a.RespiratoryRate));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not store alert for {PatientId}.", a.PatientId);
        }
    }

    public async Task<IReadOnlyList<MlPredictionPoint>> GetMlPredictionHistoryAsync(string patientId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var days = RecentDays(TimeSpan.FromHours(24));
            var points = new List<MlPredictionPoint>();

            foreach (var day in days)
            {
                var rows = await session.ExecuteAsync(new SimpleStatement(
                    "SELECT recorded_at, risk_probability, risk_category FROM ml_predictions WHERE patient_id = ? AND prediction_day = ? LIMIT 200",
                    patientId, day));

                foreach (var row in rows)
                {
                    points.Add(new MlPredictionPoint(
                        new DateTimeOffset(row.GetValue<DateTime>("recorded_at"), TimeSpan.Zero),
                        GetDouble(row, "risk_probability"),
                        GetString(row, "risk_category")));
                }
            }

            return points.OrderBy(p => p.Timestamp).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load ML prediction history for {PatientId}.", patientId);
            return Array.Empty<MlPredictionPoint>();
        }
    }

    private static PatientProfileDto ReadProfile(Row row) => new(
        GetString(row, "patient_id"),
        GetString(row, "patient_name"),
        GetString(row, "room_number"),
        GetInt(row, "age"),
        GetString(row, "sex"),
        GetInt(row, "cholesterol"),
        GetInt(row, "diabetes"),
        GetInt(row, "family_history"),
        GetInt(row, "smoking"),
        GetInt(row, "obesity"),
        GetInt(row, "alcohol_consumption"),
        GetDouble(row, "exercise_hours_per_week"),
        GetString(row, "diet"),
        GetInt(row, "previous_heart_problems"),
        GetInt(row, "medication_use"),
        GetInt(row, "stress_level"),
        GetDouble(row, "sedentary_hours_per_day"),
        GetDouble(row, "bmi"),
        GetInt(row, "triglycerides"),
        GetInt(row, "physical_activity_days_per_week"),
        GetInt(row, "sleep_hours_per_day"));

    // ---------------- Legacy statistics page ----------------

    public async Task<HealthStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var since = DateTimeOffset.UtcNow.AddHours(-24);
            var days = RecentDays(TimeSpan.FromHours(24));
            var heartRates = new List<HeartRatePoint>();
            var temperatures = new List<double>();
            var alertCounts = new List<RoomAlertCount>();

            foreach (var room in Rooms)
            {
                var roomAlertCount = 0;

                foreach (var day in days)
                {
                    var vitals = await session.ExecuteAsync(new SimpleStatement(
                        "SELECT recorded_at, heart_rate, temperature FROM patient_vitals WHERE room_number = ? AND vital_day = ?",
                        room, day));

                    foreach (var row in vitals)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var recordedAt = new DateTimeOffset(row.GetValue<DateTime>("recorded_at"), TimeSpan.Zero);
                        if (recordedAt < since) continue;

                        heartRates.Add(new HeartRatePoint(recordedAt, GetInt(row, "heart_rate"), room));
                        temperatures.Add(GetDouble(row, "temperature"));
                    }

                    var alerts = await session.ExecuteAsync(new SimpleStatement(
                        "SELECT alert_id FROM alerts_log WHERE room_number = ? AND alert_day = ?", room, day));
                    roomAlertCount += alerts.Count();
                }

                alertCounts.Add(new RoomAlertCount(room, roomAlertCount));
            }

            return new HealthStatistics(
                heartRates.OrderBy(point => point.Timestamp).TakeLast(240).ToArray(),
                alertCounts,
                temperatures.Count == 0 ? 0 : Math.Round(temperatures.Average(), 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load Cassandra statistics. Returning empty dashboard data.");
            return new HealthStatistics(
                Array.Empty<HeartRatePoint>(),
                Rooms.Select(room => new RoomAlertCount(room, 0)).ToArray(),
                0);
        }
    }

    // ---------------- Helpers ----------------

    private async Task<IReadOnlyList<VitalSample>> ScanRecentVitalsAsync(Cassandra.ISession session, TimeSpan window, int perPartitionLimit)
    {
        var since = DateTimeOffset.UtcNow - window;
        var days = RecentDays(window);
        var result = new List<VitalSample>();

        foreach (var room in Rooms)
        {
            foreach (var day in days)
            {
                var rows = await session.ExecuteAsync(new SimpleStatement(
                    "SELECT * FROM patient_vitals WHERE room_number = ? AND vital_day = ? LIMIT ?", room, day, perPartitionLimit));

                foreach (var row in rows)
                {
                    var sample = ReadVital(row);
                    if (sample.Timestamp >= since)
                    {
                        result.Add(sample);
                    }
                }
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<AlertSample>> ScanRecentAlertsAsync(Cassandra.ISession session, TimeSpan window, int perPartitionLimit)
    {
        var since = DateTimeOffset.UtcNow - window;
        var days = RecentDays(window);
        var result = new List<AlertSample>();

        foreach (var room in Rooms)
        {
            foreach (var day in days)
            {
                var rows = await session.ExecuteAsync(new SimpleStatement(
                    "SELECT * FROM alerts_log WHERE room_number = ? AND alert_day = ? LIMIT ?", room, day, perPartitionLimit));

                foreach (var row in rows)
                {
                    var ts = new DateTimeOffset(row.GetValue<DateTime>("recorded_at"), TimeSpan.Zero);
                    if (ts < since) continue;

                    result.Add(new AlertSample(
                        row.GetValue<Guid>("alert_id"),
                        GetString(row, "patient_id"),
                        room,
                        GetString(row, "alert_type"),
                        GetString(row, "severity"),
                        GetString(row, "message"),
                        GetDouble(row, "value"),
                        GetInt(row, "heart_rate"),
                        GetInt(row, "spo2"),
                        GetDouble(row, "temperature"),
                        GetInt(row, "systolic_bp"),
                        GetInt(row, "diastolic_bp"),
                        GetInt(row, "respiratory_rate"),
                        ts));
                }
            }
        }

        return result;
    }

    private static async Task<Dictionary<string, RiskSample>> GetLatestRiskByPatientAsync(
        Cassandra.ISession session, IEnumerable<string> patientIds, LocalDate[] days)
    {
        var result = new Dictionary<string, RiskSample>();

        foreach (var patientId in patientIds.Distinct())
        {
            foreach (var day in days)
            {
                var rows = await session.ExecuteAsync(new SimpleStatement(
                    "SELECT * FROM ai_risk_scores WHERE patient_id = ? AND score_day = ? LIMIT 1", patientId, day));

                var row = rows.FirstOrDefault();
                if (row is null) continue;

                var ts = new DateTimeOffset(row.GetValue<DateTime>("recorded_at"), TimeSpan.Zero);
                if (!result.TryGetValue(patientId, out var existing) || ts > existing.Timestamp)
                {
                    result[patientId] = new RiskSample(
                        patientId, GetString(row, "room_number"), GetInt(row, "score"),
                        GetString(row, "category"), GetString(row, "factors"), ts);
                }
            }
        }

        return result;
    }

    private static async Task<List<RiskSample>> GetRiskHistoryAsync(Cassandra.ISession session, string patientId, DateTimeOffset since, LocalDate[] days)
    {
        var result = new List<RiskSample>();

        foreach (var day in days)
        {
            var rows = await session.ExecuteAsync(new SimpleStatement(
                "SELECT * FROM ai_risk_scores WHERE patient_id = ? AND score_day = ?", patientId, day));

            foreach (var row in rows)
            {
                var ts = new DateTimeOffset(row.GetValue<DateTime>("recorded_at"), TimeSpan.Zero);
                if (ts < since) continue;

                result.Add(new RiskSample(
                    patientId, GetString(row, "room_number"), GetInt(row, "score"),
                    GetString(row, "category"), GetString(row, "factors"), ts));
            }
        }

        return result.OrderBy(r => r.Timestamp).ToList();
    }

    private static async Task<IReadOnlyList<PatientInfo>> LoadPatientsAsync(Cassandra.ISession session)
    {
        var rows = await session.ExecuteAsync(new SimpleStatement("SELECT patient_id, patient_name, room_number, age FROM patients"));
        return rows.Select(row => new PatientInfo(
            GetString(row, "patient_id"), GetString(row, "patient_name"), GetString(row, "room_number"), GetInt(row, "age")))
            .ToArray();
    }

    private static async Task<PatientInfo?> GetPatientAsync(Cassandra.ISession session, string patientId)
    {
        var rows = await session.ExecuteAsync(new SimpleStatement(
            "SELECT patient_id, patient_name, room_number, age FROM patients WHERE patient_id = ?", patientId));
        var row = rows.FirstOrDefault();
        return row is null ? null : new PatientInfo(
            GetString(row, "patient_id"), GetString(row, "patient_name"), GetString(row, "room_number"), GetInt(row, "age"));
    }

    private static VitalSample ReadVital(Row row) => new(
        GetString(row, "patient_id"),
        GetString(row, "patient_name"),
        GetString(row, "room_number"),
        GetInt(row, "age"),
        GetInt(row, "heart_rate"),
        GetInt(row, "spo2"),
        GetDouble(row, "temperature"),
        GetInt(row, "systolic_bp"),
        GetInt(row, "diastolic_bp"),
        GetInt(row, "respiratory_rate"),
        new DateTimeOffset(row.GetValue<DateTime>("recorded_at"), TimeSpan.Zero));

    private static string Classify(VitalSample v)
    {
        if (v.HeartRate > 120 || v.HeartRate < 50 || v.Spo2 < 90 || v.Temperature > 38.5
            || v.SystolicBp > 140 || v.DiastolicBp > 90 || v.RespiratoryRate > 24 || (v.RespiratoryRate > 0 && v.RespiratoryRate < 10))
        {
            return "Critical";
        }

        if (v.HeartRate > 110 || v.HeartRate < 55 || v.Spo2 < 94 || v.Temperature > 37.8
            || v.SystolicBp > 130 || v.DiastolicBp > 85 || v.RespiratoryRate > 22 || (v.RespiratoryRate > 0 && v.RespiratoryRate < 11))
        {
            return "Warning";
        }

        return "Normal";
    }

    private static VitalStat Stat(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return new VitalStat(0, 0, 0);
        return new VitalStat(Math.Round(list.Min(), 1), Math.Round(list.Max(), 1), Math.Round(list.Average(), 1));
    }

    private static List<VitalSample> Downsample(List<VitalSample> source, int maxPoints)
    {
        if (source.Count <= maxPoints) return source;
        var step = (int)Math.Ceiling(source.Count / (double)maxPoints);
        return source.Where((_, i) => i % step == 0).ToList();
    }

    private static LocalDate[] RecentDays(TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var earliest = now - window;
        var days = new List<LocalDate>();
        for (var d = earliest.UtcDateTime.Date; d <= now.UtcDateTime.Date; d = d.AddDays(1))
        {
            days.Add(new LocalDate(d.Year, d.Month, d.Day));
        }
        days.Reverse();
        return days.ToArray();
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

    private static int GetInt(Row row, string col) => row.IsNull(col) ? 0 : row.GetValue<int>(col);
    private static double GetDouble(Row row, string col) => row.IsNull(col) ? 0 : row.GetValue<double>(col);
    private static string GetString(Row row, string col) => row.IsNull(col) ? string.Empty : row.GetValue<string>(col);

    private static LocalDate ToCassandraDate(DateTimeOffset timestamp)
    {
        var date = timestamp.UtcDateTime.Date;
        return new LocalDate(date.Year, date.Month, date.Day);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session.IsValueCreated)
        {
            var session = await _session.Value;
            await session.ShutdownAsync();
        }

        _cluster.Dispose();
    }

    // Internal projections.
    private sealed record VitalSample(string PatientId, string PatientName, string RoomNumber, int Age,
        int HeartRate, int Spo2, double Temperature, int SystolicBp, int DiastolicBp, int RespiratoryRate, DateTimeOffset Timestamp);

    private sealed record AlertSample(Guid AlertId, string PatientId, string RoomNumber, string AlertType, string Severity,
        string Message, double Value, int HeartRate, int Spo2, double Temperature, int SystolicBp, int DiastolicBp, int RespiratoryRate, DateTimeOffset Timestamp);

    private sealed record RiskSample(string PatientId, string RoomNumber, int Score, string Category, string Factors, DateTimeOffset Timestamp);

    private sealed record PatientInfo(string PatientId, string PatientName, string RoomNumber, int Age);
}

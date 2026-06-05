using Cassandra;

// Cassandra writer for the streaming processor.
// Extends the original patient_vitals / alerts_log tables (non-destructively) and adds the
// patients, ai_risk_scores and vitals_window_agg tables required by the expanded system.
public sealed class CassandraHealthRepository : IAsyncDisposable
{
    private readonly ICluster _cluster;
    private readonly Cassandra.ISession _session;
    private readonly PreparedStatement _insertVital;
    private readonly PreparedStatement _insertAlert;
    private readonly PreparedStatement _insertRisk;
    private readonly PreparedStatement _insertWindow;
    private readonly PreparedStatement _upsertPatient;

    private CassandraHealthRepository(
        ICluster cluster,
        Cassandra.ISession session,
        PreparedStatement insertVital,
        PreparedStatement insertAlert,
        PreparedStatement insertRisk,
        PreparedStatement insertWindow,
        PreparedStatement upsertPatient)
    {
        _cluster = cluster;
        _session = session;
        _insertVital = insertVital;
        _insertAlert = insertAlert;
        _insertRisk = insertRisk;
        _insertWindow = insertWindow;
        _upsertPatient = upsertPatient;
    }

    public static async Task<CassandraHealthRepository> CreateAsync(CassandraOptions options)
    {
        var cluster = Cluster.Builder()
            .AddContactPoint(options.ContactPoint)
            .WithPort(options.Port)
            .Build();

        var systemSession = await cluster.ConnectAsync();
        await systemSession.ExecuteAsync(new SimpleStatement(
            $"CREATE KEYSPACE IF NOT EXISTS {options.Keyspace} WITH replication = {{ 'class': 'SimpleStrategy', 'replication_factor': 1 }}"));
        await systemSession.ShutdownAsync();

        var session = await cluster.ConnectAsync(options.Keyspace);

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS patient_vitals (
              room_number text,
              vital_day date,
              recorded_at timestamp,
              patient_id text,
              patient_name text,
              age int,
              heart_rate int,
              spo2 int,
              temperature double,
              systolic_bp int,
              diastolic_bp int,
              respiratory_rate int,
              PRIMARY KEY ((room_number, vital_day), recorded_at, patient_id)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC, patient_id ASC)
            """));

        // Non-destructive migration for deployments created before the metric set was expanded.
        await SafeAddColumnsAsync(session, "patient_vitals", new()
        {
            ["age"] = "int",
            ["systolic_bp"] = "int",
            ["diastolic_bp"] = "int",
            ["respiratory_rate"] = "int"
        });

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS alerts_log (
              room_number text,
              alert_day date,
              recorded_at timestamp,
              alert_id uuid,
              patient_id text,
              alert_type text,
              severity text,
              message text,
              value double,
              heart_rate int,
              spo2 int,
              temperature double,
              systolic_bp int,
              diastolic_bp int,
              respiratory_rate int,
              PRIMARY KEY ((room_number, alert_day), recorded_at, alert_id)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC, alert_id ASC)
            """));

        await SafeAddColumnsAsync(session, "alerts_log", new()
        {
            ["alert_type"] = "text",
            ["value"] = "double",
            ["systolic_bp"] = "int",
            ["diastolic_bp"] = "int",
            ["respiratory_rate"] = "int"
        });

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS patients (
              patient_id text PRIMARY KEY,
              patient_name text,
              room_number text,
              age int,
              sex text,
              cholesterol int,
              diabetes int,
              family_history int,
              smoking int,
              obesity int,
              alcohol_consumption int,
              exercise_hours_per_week double,
              diet text,
              previous_heart_problems int,
              medication_use int,
              stress_level int,
              sedentary_hours_per_day double,
              bmi double,
              triglycerides int,
              physical_activity_days_per_week int,
              sleep_hours_per_day int
            )
            """));

        // Non-destructive migration of the patients table for pre-existing deployments.
        await SafeAddColumnsAsync(session, "patients", new()
        {
            ["sex"] = "text",
            ["cholesterol"] = "int",
            ["diabetes"] = "int",
            ["family_history"] = "int",
            ["smoking"] = "int",
            ["obesity"] = "int",
            ["alcohol_consumption"] = "int",
            ["exercise_hours_per_week"] = "double",
            ["diet"] = "text",
            ["previous_heart_problems"] = "int",
            ["medication_use"] = "int",
            ["stress_level"] = "int",
            ["sedentary_hours_per_day"] = "double",
            ["bmi"] = "double",
            ["triglycerides"] = "int",
            ["physical_activity_days_per_week"] = "int",
            ["sleep_hours_per_day"] = "int"
        });

        // Stores the ML heart-attack prediction history for trend visualisation.
        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS ml_predictions (
              patient_id text,
              prediction_day date,
              recorded_at timestamp,
              room_number text,
              risk_probability double,
              risk_category text,
              top_factors text,
              PRIMARY KEY ((patient_id, prediction_day), recorded_at)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS ai_risk_scores (
              patient_id text,
              score_day date,
              recorded_at timestamp,
              room_number text,
              score int,
              category text,
              factors text,
              heart_rate int,
              spo2 int,
              temperature double,
              systolic_bp int,
              diastolic_bp int,
              respiratory_rate int,
              PRIMARY KEY ((patient_id, score_day), recorded_at)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS vitals_window_agg (
              room_number text,
              window_day date,
              window_start timestamp,
              patient_id text,
              avg_heart_rate double,
              avg_spo2 double,
              avg_temperature double,
              avg_systolic double,
              avg_diastolic double,
              avg_respiratory double,
              sample_count int,
              PRIMARY KEY ((room_number, window_day), window_start, patient_id)
            ) WITH CLUSTERING ORDER BY (window_start DESC, patient_id ASC)
            """));

        var insertVital = await session.PrepareAsync("""
            INSERT INTO patient_vitals
            (room_number, vital_day, recorded_at, patient_id, patient_name, age, heart_rate, spo2, temperature, systolic_bp, diastolic_bp, respiratory_rate)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """);

        var insertAlert = await session.PrepareAsync("""
            INSERT INTO alerts_log
            (room_number, alert_day, recorded_at, alert_id, patient_id, alert_type, severity, message, value, heart_rate, spo2, temperature, systolic_bp, diastolic_bp, respiratory_rate)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """);

        var insertRisk = await session.PrepareAsync("""
            INSERT INTO ai_risk_scores
            (patient_id, score_day, recorded_at, room_number, score, category, factors, heart_rate, spo2, temperature, systolic_bp, diastolic_bp, respiratory_rate)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """);

        var insertWindow = await session.PrepareAsync("""
            INSERT INTO vitals_window_agg
            (room_number, window_day, window_start, patient_id, avg_heart_rate, avg_spo2, avg_temperature, avg_systolic, avg_diastolic, avg_respiratory, sample_count)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """);

        var upsertPatient = await session.PrepareAsync("""
            INSERT INTO patients
            (patient_id, patient_name, room_number, age, sex, cholesterol, diabetes, family_history, smoking, obesity,
             alcohol_consumption, exercise_hours_per_week, diet, previous_heart_problems, medication_use, stress_level,
             sedentary_hours_per_day, bmi, triglycerides, physical_activity_days_per_week, sleep_hours_per_day)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """);

        return new CassandraHealthRepository(cluster, session, insertVital, insertAlert, insertRisk, insertWindow, upsertPatient);
    }

    private static async Task SafeAddColumnsAsync(Cassandra.ISession session, string table, Dictionary<string, string> columns)
    {
        foreach (var (name, type) in columns)
        {
            try
            {
                await session.ExecuteAsync(new SimpleStatement($"ALTER TABLE {table} ADD {name} {type}"));
            }
            catch (InvalidQueryException)
            {
                // Column already exists - safe to ignore on existing deployments.
            }
        }
    }

    public async Task InsertVitalAsync(VitalReading reading)
    {
        var statement = _insertVital.Bind(
            reading.RoomNumber,
            ToCassandraDate(reading.RecordedAt),
            reading.RecordedAt.UtcDateTime,
            reading.PatientId,
            reading.PatientName,
            reading.Age,
            reading.HeartRate,
            reading.Spo2,
            reading.Temperature,
            reading.SystolicBp,
            reading.DiastolicBp,
            reading.RespiratoryRate);

        await _session.ExecuteAsync(statement);
    }

    public async Task InsertAlertAsync(AlertMessage alert)
    {
        var statement = _insertAlert.Bind(
            alert.RoomNumber,
            ToCassandraDate(alert.RecordedAt),
            alert.RecordedAt.UtcDateTime,
            alert.AlertId,
            alert.PatientId,
            alert.AlertType,
            alert.Severity,
            alert.Message,
            alert.Value,
            alert.HeartRate,
            alert.Spo2,
            alert.Temperature,
            alert.SystolicBp,
            alert.DiastolicBp,
            alert.RespiratoryRate);

        await _session.ExecuteAsync(statement);
    }

    public async Task InsertRiskAsync(RiskScore risk)
    {
        var statement = _insertRisk.Bind(
            risk.PatientId,
            ToCassandraDate(risk.RecordedAt),
            risk.RecordedAt.UtcDateTime,
            risk.RoomNumber,
            risk.Score,
            risk.Category,
            risk.Factors,
            risk.HeartRate,
            risk.Spo2,
            risk.Temperature,
            risk.SystolicBp,
            risk.DiastolicBp,
            risk.RespiratoryRate);

        await _session.ExecuteAsync(statement);
    }

    public async Task InsertWindowAsync(WindowAggregate window)
    {
        var statement = _insertWindow.Bind(
            window.RoomNumber,
            ToCassandraDate(window.WindowStart),
            window.WindowStart.UtcDateTime,
            window.PatientId,
            window.AvgHeartRate,
            window.AvgSpo2,
            window.AvgTemperature,
            window.AvgSystolic,
            window.AvgDiastolic,
            window.AvgRespiratory,
            window.SampleCount);

        await _session.ExecuteAsync(statement);
    }

    public async Task UpsertPatientAsync(VitalReading reading)
    {
        var statement = _upsertPatient.Bind(
            reading.PatientId,
            reading.PatientName,
            reading.RoomNumber,
            reading.Age,
            reading.Sex,
            reading.Cholesterol,
            reading.Diabetes,
            reading.FamilyHistory,
            reading.Smoking,
            reading.Obesity,
            reading.AlcoholConsumption,
            reading.ExerciseHoursPerWeek,
            reading.Diet,
            reading.PreviousHeartProblems,
            reading.MedicationUse,
            reading.StressLevel,
            reading.SedentaryHoursPerDay,
            reading.Bmi,
            reading.Triglycerides,
            reading.PhysicalActivityDaysPerWeek,
            reading.SleepHoursPerDay);

        await _session.ExecuteAsync(statement);
    }

    private static LocalDate ToCassandraDate(DateTimeOffset timestamp)
    {
        var date = timestamp.UtcDateTime.Date;
        return new LocalDate(date.Year, date.Month, date.Day);
    }

    public async ValueTask DisposeAsync()
    {
        await _session.ShutdownAsync();
        _cluster.Dispose();
    }
}

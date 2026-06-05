// Shared records for the Spark/Kafka streaming processor.
// Kept in the global namespace to match the existing single-file Program.cs style.

public sealed record KafkaOptions
{
    public string BootstrapServers { get; init; } = "178.105.181.143:9092";
    public string Topic { get; init; } = "health-vitals";
}

public sealed record CassandraOptions
{
    public string ContactPoint { get; init; } = "178.105.181.143";
    public int Port { get; init; } = 9042;
    public string Keyspace { get; init; } = "smart_health";
}

public sealed record SignalROptions
{
    public string HubUrl { get; init; } = "http://localhost:5084/healthHub";
}

public sealed record VitalReading(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int Age,
    int HeartRate,
    int Spo2,
    double Temperature,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    DateTimeOffset RecordedAt,
    // Clinical profile (heart-attack dataset features) - optional for backward compatibility.
    string Sex = "Unknown",
    int Cholesterol = 0,
    int Diabetes = 0,
    int FamilyHistory = 0,
    int Smoking = 0,
    int Obesity = 0,
    int AlcoholConsumption = 0,
    double ExerciseHoursPerWeek = 0,
    string Diet = "Average",
    int PreviousHeartProblems = 0,
    int MedicationUse = 0,
    int StressLevel = 0,
    double SedentaryHoursPerDay = 0,
    double Bmi = 0,
    int Triglycerides = 0,
    int PhysicalActivityDaysPerWeek = 0,
    int SleepHoursPerDay = 0)
{
    public static VitalReading From(Microsoft.Spark.Sql.Row row)
    {
        var recordedAt = row.GetAs<DateTime>("recordedAt");
        return new VitalReading(
            row.GetAs<string>("patientId"),
            row.GetAs<string>("patientName"),
            row.GetAs<string>("roomNumber"),
            row.GetAs<int>("age"),
            row.GetAs<int>("heartRate"),
            row.GetAs<int>("spo2"),
            row.GetAs<double>("temperature"),
            row.GetAs<int>("systolicBp"),
            row.GetAs<int>("diastolicBp"),
            row.GetAs<int>("respiratoryRate"),
            new DateTimeOffset(DateTime.SpecifyKind(recordedAt, DateTimeKind.Utc)));
    }
}

// Full clinical profile persisted to the patients table and fed to the heart-attack ML model.
public sealed record PatientProfile(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int Age,
    string Sex,
    int Cholesterol,
    int Diabetes,
    int FamilyHistory,
    int Smoking,
    int Obesity,
    int AlcoholConsumption,
    double ExerciseHoursPerWeek,
    string Diet,
    int PreviousHeartProblems,
    int MedicationUse,
    int StressLevel,
    double SedentaryHoursPerDay,
    double Bmi,
    int Triglycerides,
    int PhysicalActivityDaysPerWeek,
    int SleepHoursPerDay);

// A single alert produced by the rule engine. AlertType identifies the metric that
// breached a threshold and Value carries the offending measurement.
public sealed record AlertMessage(
    Guid AlertId,
    string PatientId,
    string RoomNumber,
    string AlertType,
    string Severity,
    string Message,
    double Value,
    int HeartRate,
    int Spo2,
    double Temperature,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    DateTimeOffset RecordedAt);

// AI risk assessment for a reading.
public sealed record RiskScore(
    string PatientId,
    string RoomNumber,
    int Score,
    string Category,
    string Factors,
    int HeartRate,
    int Spo2,
    double Temperature,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    DateTimeOffset RecordedAt);

// Aggregated values for a sliding/tumbling window (analytics + Spark windowing demo).
public sealed record WindowAggregate(
    string PatientId,
    string RoomNumber,
    DateTimeOffset WindowStart,
    double AvgHeartRate,
    double AvgSpo2,
    double AvgTemperature,
    double AvgSystolic,
    double AvgDiastolic,
    double AvgRespiratory,
    int SampleCount);

// Lightweight metrics snapshot pushed to the dashboard System Health page.
public sealed record StreamMetrics(
    long MessagesProcessed,
    double MessagesPerSecond,
    int LastBatchSize,
    double AvgProcessingMs,
    long WindowsComputed,
    long AlertsGenerated,
    DateTimeOffset RecordedAt);

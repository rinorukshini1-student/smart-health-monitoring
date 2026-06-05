namespace Web.Dashboard.Models;

// AI risk assessment pushed from the streaming processor and stored in Cassandra.
public sealed record RiskScoreDto(
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

// Streaming performance metrics for the System Health page.
public sealed record StreamMetricsDto(
    long MessagesProcessed,
    double MessagesPerSecond,
    int LastBatchSize,
    double AvgProcessingMs,
    long WindowsComputed,
    long AlertsGenerated,
    DateTimeOffset RecordedAt);

// ---------- Page 1: Overview ----------

public sealed record TimeSeriesPoint(string Label, double Value);

public sealed record RecentEvent(string Kind, string PatientId, string RoomNumber, string Text, string Severity, DateTimeOffset Timestamp);

public sealed record OverviewDto(
    int TotalPatients,
    int ActivePatients,
    int CriticalAlerts,
    double AverageHeartRate,
    double AverageTemperature,
    double AverageSpo2,
    int HighRiskPatients,
    IReadOnlyList<TimeSeriesPoint> MessagesPerMinute,
    IReadOnlyList<TimeSeriesPoint> AlertsPerHour,
    IReadOnlyList<TimeSeriesPoint> HeartRateTrend,
    IReadOnlyList<TimeSeriesPoint> TemperatureTrend,
    IReadOnlyList<RecentEvent> RecentReadings,
    IReadOnlyList<RecentEvent> RecentAlerts);

// ---------- Page 2: Live Monitoring ----------

public sealed record LivePatientRow(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int Age,
    int HeartRate,
    double Temperature,
    int Spo2,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    int RiskScore,
    string RiskCategory,
    string Status,
    DateTimeOffset LastUpdate);

// ---------- Page 3: Patient Details ----------

public sealed record VitalStat(double Min, double Max, double Average);

public sealed record RiskPoint(DateTimeOffset Timestamp, int Score, string Category);

public sealed record PatientDetailDto(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int Age,
    string Range,
    IReadOnlyList<DateTimeOffset> Timestamps,
    IReadOnlyList<int> HeartRates,
    IReadOnlyList<double> Temperatures,
    IReadOnlyList<int> Spo2s,
    IReadOnlyList<int> Systolics,
    IReadOnlyList<int> Diastolics,
    IReadOnlyList<int> RespiratoryRates,
    VitalStat HeartRateStat,
    VitalStat TemperatureStat,
    VitalStat Spo2Stat,
    VitalStat BloodPressureStat,
    int CurrentRiskScore,
    string CurrentRiskCategory,
    string RiskFactors,
    IReadOnlyList<RiskPoint> RiskHistory);

// ---------- Page 5: Analytics ----------

public sealed record RiskLeaderRow(string PatientId, string RoomNumber, int Score, string Category, string Factors);

public sealed record AnalyticsDto(
    IReadOnlyList<TimeSeriesPoint> AvgHeartRatePerWindow,
    IReadOnlyList<TimeSeriesPoint> AvgTemperaturePerWindow,
    IReadOnlyList<TimeSeriesPoint> AvgSpo2PerWindow,
    IReadOnlyList<TimeSeriesPoint> AlertFrequencyByType,
    IReadOnlyList<RiskLeaderRow> HighestRiskPatients,
    int TotalWindows);

// ---------- Heart-attack ML module ----------

// Full clinical profile (heart-attack dataset features) stored in the patients table.
public sealed record PatientProfileDto(
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

// Output of the heart-attack prediction service.
public sealed record MlPredictionDto(
    string PatientId,
    string PatientName,
    string RoomNumber,
    double RiskProbability,
    string RiskCategory,
    string Prediction,
    IReadOnlyList<string> TopFactors,
    int HeartRate,
    int SystolicBp,
    int DiastolicBp,
    DateTimeOffset RecordedAt);

public sealed record MlPredictionPoint(DateTimeOffset Timestamp, double RiskProbability, string RiskCategory);

// ---------- Page 6: System Health ----------

public sealed record SystemMetricsDto(
    long KafkaMessagesReceived,
    double KafkaMessagesPerSecond,
    int SparkLastBatchSize,
    double SparkAvgProcessingMs,
    long SparkWindowsComputed,
    long StoredVitals,
    long StoredAlerts,
    double CassandraWriteRatePerSecond,
    double ApiAverageResponseMs,
    long AlertsGenerated,
    bool StreamingOnline,
    DateTimeOffset Timestamp);

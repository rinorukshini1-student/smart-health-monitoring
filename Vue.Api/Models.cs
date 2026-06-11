namespace Vue.Api;

public sealed record HeartRatePoint(DateTimeOffset Timestamp, int HeartRate, string RoomNumber);

public sealed record RoomAlertCount(string RoomNumber, int Count);

public sealed record HealthStatistics(
    IReadOnlyList<HeartRatePoint> HeartRates,
    IReadOnlyList<RoomAlertCount> AlertsByRoom,
    double AverageTemperature);

// ---------------- Core telemetry ----------------

public sealed record VitalReadingDto(
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
    DateTimeOffset RecordedAt);

public sealed record AlertMessageDto(
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

public sealed record StreamMetricsDto(
    long MessagesProcessed,
    double MessagesPerSecond,
    int LastBatchSize,
    double AvgProcessingMs,
    long WindowsComputed,
    long AlertsGenerated,
    DateTimeOffset RecordedAt);

// ---------------- Patient profile (heart-attack dataset features) ----------------

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

// ---------------- Page DTOs ----------------

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

public sealed record RiskLeaderRow(string PatientId, string RoomNumber, int Score, string Category, string Factors);

public sealed record AnalyticsDto(
    IReadOnlyList<TimeSeriesPoint> AvgHeartRatePerWindow,
    IReadOnlyList<TimeSeriesPoint> AvgTemperaturePerWindow,
    IReadOnlyList<TimeSeriesPoint> AvgSpo2PerWindow,
    IReadOnlyList<TimeSeriesPoint> AlertFrequencyByType,
    IReadOnlyList<RiskLeaderRow> HighestRiskPatients,
    int TotalWindows);

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

// ---------------- Heart-attack ML ----------------

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

// Result of an on-demand heart-attack risk prediction (POST /api/ai/predict).
public sealed record HeartRiskResult(
    bool PredictedLabel,
    float Probability,
    float Score,
    string RiskLevel,
    string Recommendation,
    List<string> MainFactors);

// Patient definition with static clinical profile used by the data generator.
public sealed record PatientDefinition(
    string PatientId, string PatientName, string RoomNumber, int Age, string Sex,
    int Cholesterol, int Diabetes, int FamilyHistory, int Smoking, int Obesity, int AlcoholConsumption,
    double ExerciseHoursPerWeek, string Diet, int PreviousHeartProblems, int MedicationUse, int StressLevel,
    double SedentaryHoursPerDay, double Bmi, int Triglycerides, int PhysicalActivityDaysPerWeek, int SleepHoursPerDay)
{
    public PatientProfileDto ToProfile() => new(
        PatientId, PatientName, RoomNumber, Age, Sex, Cholesterol, Diabetes, FamilyHistory, Smoking, Obesity,
        AlcoholConsumption, ExerciseHoursPerWeek, Diet, PreviousHeartProblems, MedicationUse, StressLevel,
        SedentaryHoursPerDay, Bmi, Triglycerides, PhysicalActivityDaysPerWeek, SleepHoursPerDay);
}

namespace Vue.Api.Data;

public interface IHealthStatsRepository
{
    Task<HealthStatistics> GetStatisticsAsync(CancellationToken cancellationToken);

    Task<OverviewDto> GetOverviewAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LivePatientRow>> GetLiveSnapshotAsync(CancellationToken cancellationToken);

    Task<PatientDetailDto?> GetPatientDetailAsync(string patientId, string range, CancellationToken cancellationToken);

    Task<IReadOnlyList<AlertMessageDto>> GetAlertsAsync(string? severity, string? type, string? query, int limit, CancellationToken cancellationToken);

    Task<AnalyticsDto> GetAnalyticsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RiskLeaderRow>> GetHighRiskPatientsAsync(int limit, CancellationToken cancellationToken);

    Task<(long vitals, long alerts)> GetStoredCountsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PatientProfileDto>> GetPatientProfilesAsync(CancellationToken cancellationToken);

    Task<PatientProfileDto?> GetPatientProfileAsync(string patientId, CancellationToken cancellationToken);

    Task StoreMlPredictionAsync(MlPredictionDto prediction, CancellationToken cancellationToken);

    Task StoreAlertAsync(AlertMessageDto alert, CancellationToken cancellationToken);

    Task<IReadOnlyList<MlPredictionPoint>> GetMlPredictionHistoryAsync(string patientId, CancellationToken cancellationToken);
}

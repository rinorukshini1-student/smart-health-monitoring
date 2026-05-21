using Web.Dashboard.Models;

namespace Web.Dashboard.Data;

public interface IHealthStatsRepository
{
    Task<HealthStatistics> GetStatisticsAsync(CancellationToken cancellationToken);
}

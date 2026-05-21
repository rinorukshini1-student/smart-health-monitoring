using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.Dashboard.Data;
using Web.Dashboard.Models;

namespace Web.Dashboard.Pages;

public sealed class StatisticsModel : PageModel
{
    private readonly IHealthStatsRepository _repository;

    public StatisticsModel(IHealthStatsRepository repository)
    {
        _repository = repository;
    }

    public HealthStatistics Statistics { get; private set; } = new(
        Array.Empty<HeartRatePoint>(),
        Array.Empty<RoomAlertCount>(),
        0);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Statistics = await _repository.GetStatisticsAsync(cancellationToken);
    }
}

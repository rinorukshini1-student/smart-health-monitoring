using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.Dashboard.Data;
using Web.Dashboard.Models;

namespace Web.Dashboard.Pages;

public class IndexModel : PageModel
{
    private readonly IHealthStatsRepository _repository;

    public IndexModel(IHealthStatsRepository repository)
    {
        _repository = repository;
    }

    public OverviewDto Overview { get; private set; } = new(0, 0, 0, 0, 0, 0, 0,
        Array.Empty<TimeSeriesPoint>(), Array.Empty<TimeSeriesPoint>(),
        Array.Empty<TimeSeriesPoint>(), Array.Empty<TimeSeriesPoint>(),
        Array.Empty<RecentEvent>(), Array.Empty<RecentEvent>());

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Overview = await _repository.GetOverviewAsync(cancellationToken);
    }
}

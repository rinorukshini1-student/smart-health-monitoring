using Microsoft.AspNetCore.SignalR;
using Web.Dashboard.Models;
using Web.Dashboard.Services;

namespace Web.Dashboard.Hubs;

public sealed class HealthHub : Hub
{
    private readonly SystemMetricsService _metrics;

    public HealthHub(SystemMetricsService metrics)
    {
        _metrics = metrics;
    }

    public async Task PublishVitals(VitalReadingDto reading)
    {
        _metrics.RecordVital();
        await Clients.All.SendAsync("vitalsReceived", reading);
    }

    public async Task PublishAlert(AlertMessageDto alert)
    {
        _metrics.RecordAlert();
        await Clients.All.SendAsync("alertReceived", alert);
    }

    public async Task PublishRisk(RiskScoreDto risk)
    {
        await Clients.All.SendAsync("riskReceived", risk);
    }

    public async Task PublishMetrics(StreamMetricsDto metrics)
    {
        _metrics.RecordStreamMetrics(metrics);
        await Clients.All.SendAsync("metricsReceived", metrics);
    }
}

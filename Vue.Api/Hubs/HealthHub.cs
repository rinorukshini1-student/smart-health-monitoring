using Microsoft.AspNetCore.SignalR;
using Vue.Api.Services;

namespace Vue.Api.Hubs;

public sealed class HealthHub : Hub
{
    private readonly SystemMetricsService _metrics;
    private readonly FirebasePushService _push;

    public HealthHub(SystemMetricsService metrics, FirebasePushService push)
    {
        _metrics = metrics;
        _push = push;
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
        await _push.NotifyAlertAsync(alert);
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

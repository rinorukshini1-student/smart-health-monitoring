using Microsoft.AspNetCore.SignalR;
using Web.Dashboard.Models;

namespace Web.Dashboard.Hubs;

public sealed class HealthHub : Hub
{
    public async Task PublishVitals(VitalReadingDto reading)
    {
        await Clients.All.SendAsync("vitalsReceived", reading);
    }

    public async Task PublishAlert(AlertMessageDto alert)
    {
        await Clients.All.SendAsync("alertReceived", alert);
    }
}

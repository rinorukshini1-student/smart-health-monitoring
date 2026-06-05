using Microsoft.AspNetCore.SignalR;

namespace Vue.Api.Hubs;

// The server pushes events to clients via IHubContext; no client-callable methods are required.
public sealed class HealthHub : Hub
{
}

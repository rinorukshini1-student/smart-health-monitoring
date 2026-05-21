using Web.Dashboard.Data;
using Web.Dashboard.Hubs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Web.Dashboard.Models;

var builder = WebApplication.CreateBuilder(args);
var dataProtectionKeys = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeys);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeys));
builder.Services.Configure<CassandraOptions>(builder.Configuration.GetSection("Cassandra"));
builder.Services.AddSingleton<IHealthStatsRepository, CassandraHealthStatsRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapHub<HealthHub>("/healthHub");

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/dev/rooms/sample", async (IHubContext<HealthHub> hub) =>
    {
        var now = DateTimeOffset.UtcNow;

        for (var room = 101; room <= 110; room++)
        {
            var reading = new VitalReadingDto(
                PatientId: $"P{room - 100:000}",
                PatientName: $"Patient {room - 100:00}",
                RoomNumber: room.ToString(),
                HeartRate: Random.Shared.Next(62, 104),
                Spo2: Random.Shared.Next(95, 101),
                Temperature: Math.Round(36.5 + Random.Shared.NextDouble() * 1.4, 1),
                RecordedAt: now);

            await hub.Clients.All.SendAsync("vitalsReceived", reading);
        }

        return Results.Ok(new { sent = 10 });
    });
}

app.Run();

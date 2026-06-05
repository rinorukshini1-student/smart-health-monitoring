using System.Diagnostics;
using Web.Dashboard.Data;
using Web.Dashboard.Hubs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Web.Dashboard.Models;
using Web.Dashboard.Services;

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
builder.Services.AddSingleton<SystemMetricsService>();
builder.Services.AddSingleton<Web.Dashboard.Ai.HeartAttackPredictionService>();
builder.Services.AddSingleton<MlPredictionStore>();
builder.Services.AddHostedService<MlPredictionWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Lightweight middleware to sample API response times for the System Health page.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var metrics = context.RequestServices.GetRequiredService<SystemMetricsService>();
        var stopwatch = Stopwatch.StartNew();
        await next();
        stopwatch.Stop();
        metrics.RecordApiResponse(stopwatch.Elapsed.TotalMilliseconds);
    }
    else
    {
        await next();
    }
});

app.MapRazorPages();
app.MapHub<HealthHub>("/healthHub");

// ---------------- Dashboard JSON APIs ----------------

app.MapGet("/api/overview", async (IHealthStatsRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetOverviewAsync(ct)));

app.MapGet("/api/live", async (IHealthStatsRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetLiveSnapshotAsync(ct)));

app.MapGet("/api/patients/{id}", async (string id, string? range, IHealthStatsRepository repo, CancellationToken ct) =>
{
    var detail = await repo.GetPatientDetailAsync(id, range ?? "day", ct);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapGet("/api/alerts", async (string? severity, string? type, string? q, int? limit, IHealthStatsRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetAlertsAsync(severity, type, q, limit ?? 200, ct)));

app.MapGet("/api/analytics", async (IHealthStatsRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetAnalyticsAsync(ct)));

app.MapGet("/api/high-risk", async (int? limit, IHealthStatsRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetHighRiskPatientsAsync(limit ?? 10, ct)));

app.MapGet("/api/system", async (IHealthStatsRepository repo, SystemMetricsService metrics, CancellationToken ct) =>
{
    var (vitals, alerts) = await repo.GetStoredCountsAsync(ct);
    var stream = metrics.LastStreamMetrics;

    var dto = new SystemMetricsDto(
        metrics.MessagesReceived,
        metrics.MessagesPerSecond(),
        stream?.LastBatchSize ?? 0,
        stream?.AvgProcessingMs ?? 0,
        stream?.WindowsComputed ?? 0,
        vitals,
        alerts,
        metrics.MessagesPerSecond(),
        metrics.ApiAverageResponseMs,
        stream?.AlertsGenerated ?? metrics.AlertsReceived,
        metrics.StreamingOnline,
        DateTimeOffset.UtcNow);

    return Results.Ok(dto);
});

// ---------------- Heart-attack AI APIs ----------------

app.MapGet("/api/ai/predictions", (MlPredictionStore store) => Results.Ok(store.Latest()));

app.MapGet("/api/ai/patient/{id}", async (string id, MlPredictionStore store, IHealthStatsRepository repo,
    Web.Dashboard.Ai.HeartAttackPredictionService predictor, CancellationToken ct) =>
{
    var cached = store.Get(id);
    if (cached is not null)
    {
        return Results.Ok(cached);
    }

    // Compute on demand if the worker has not produced a prediction yet.
    var profile = await repo.GetPatientProfileAsync(id, ct);
    if (profile is null)
    {
        return Results.NotFound();
    }

    var vitals = (await repo.GetLiveSnapshotAsync(ct)).FirstOrDefault(r => r.PatientId == id);
    return Results.Ok(predictor.Predict(profile, vitals));
});

app.MapGet("/api/ai/history/{id}", async (string id, IHealthStatsRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetMlPredictionHistoryAsync(id, ct)));

app.MapGet("/api/ai/factors", (Web.Dashboard.Ai.HeartAttackPredictionService predictor) =>
{
    if (predictor.Metadata is null)
    {
        return Results.Json(new { topFactors = Array.Empty<object>() });
    }

    var factors = predictor.Metadata.RootElement.GetProperty("topFactors");
    return Results.Content(factors.GetRawText(), "application/json");
});

app.MapGet("/api/ai/metrics", (Web.Dashboard.Ai.HeartAttackPredictionService predictor) =>
{
    if (predictor.Metadata is null)
    {
        return Results.Json(new { modelLoaded = predictor.ModelLoaded, chosenModel = predictor.ChosenModel, metrics = Array.Empty<object>() });
    }

    return Results.Content(predictor.Metadata.RootElement.GetRawText(), "application/json");
});

if (app.Environment.IsDevelopment())
{
    // Demo helper: pushes a batch of synthetic readings through SignalR without Kafka/Spark.
    app.MapPost("/api/dev/rooms/sample", async (IHubContext<HealthHub> hub) =>
    {
        var now = DateTimeOffset.UtcNow;

        for (var room = 101; room <= 110; room++)
        {
            var reading = new VitalReadingDto(
                PatientId: $"P{room - 100:000}",
                PatientName: $"Patient {room - 100:00}",
                RoomNumber: room.ToString(),
                Age: Random.Shared.Next(30, 80),
                HeartRate: Random.Shared.Next(62, 104),
                Spo2: Random.Shared.Next(95, 101),
                Temperature: Math.Round(36.5 + Random.Shared.NextDouble() * 1.4, 1),
                SystolicBp: Random.Shared.Next(108, 132),
                DiastolicBp: Random.Shared.Next(68, 86),
                RespiratoryRate: Random.Shared.Next(12, 19),
                RecordedAt: now);

            await hub.Clients.All.SendAsync("vitalsReceived", reading);
        }

        return Results.Ok(new { sent = 10 });
    });
}

app.Run();

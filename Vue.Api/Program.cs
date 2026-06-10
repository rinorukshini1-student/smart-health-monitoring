using System.Diagnostics;
using Vue.Api;
using Vue.Api.Ai;
using Vue.Api.Data;
using Vue.Api.Hubs;
using Vue.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var dataGenEnabled = builder.Configuration.GetValue("DataGeneration:Enabled", false);

builder.Services.Configure<CassandraOptions>(builder.Configuration.GetSection("Cassandra"));
builder.Services.Configure<FirebaseOptions>(builder.Configuration.GetSection("Firebase"));
builder.Services.AddSingleton<IHealthStatsRepository, CassandraHealthStatsRepository>();
builder.Services.AddSingleton<SystemMetricsService>();
builder.Services.AddSingleton<HeartAttackPredictionService>();
builder.Services.AddSingleton<MlPredictionStore>();
builder.Services.AddSingleton<DeviceTokenStore>();
builder.Services.AddSingleton<FirebasePushService>();

if (dataGenEnabled)
{
    builder.Services.AddSingleton(new HealthDataStore(PatientCatalog.Patients));
    builder.Services.AddHostedService<DataGeneratorService>();
}
else
{
    builder.Services.AddHostedService<MlPredictionWorker>();
}

const string DevCors = "vue-dev";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy =>
    policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors(DevCors);

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        var sw = Stopwatch.StartNew();
        await next();
        sw.Stop();

        if (dataGenEnabled)
        {
            ctx.RequestServices.GetRequiredService<HealthDataStore>().RecordApiResponse(sw.Elapsed.TotalMilliseconds);
        }
        else
        {
            ctx.RequestServices.GetRequiredService<SystemMetricsService>().RecordApiResponse(sw.Elapsed.TotalMilliseconds);
        }
    }
    else
    {
        await next();
    }
});

if (dataGenEnabled)
{
    var store = app.Services.GetRequiredService<HealthDataStore>();

    app.MapGet("/api/overview", () => store.GetOverview());
    app.MapGet("/api/live", () => store.GetLiveSnapshot());
    app.MapGet("/api/patients", () => store.GetProfiles());
    app.MapGet("/api/patients/{id}", (string id, string? range) =>
        store.GetPatientDetail(id, range ?? "day") is { } d ? Results.Ok(d) : Results.NotFound());
    app.MapGet("/api/patients/{id}/profile", (string id) =>
        store.GetProfile(id) is { } p ? Results.Ok(p) : Results.NotFound());
    app.MapGet("/api/alerts", (string? severity, string? type, string? q, int? limit) =>
        store.GetAlerts(severity, type, q, limit ?? 200));
    app.MapGet("/api/analytics", () => store.GetAnalytics());
    app.MapGet("/api/high-risk", (int? limit) => store.GetHighRisk(limit ?? 8));
    app.MapGet("/api/system", () => store.GetSystemMetrics());
    app.MapGet("/api/ai/predictions", () => store.GetMlPredictions());
    app.MapGet("/api/ai/patient/{id}", (string id) =>
        store.GetMlPrediction(id) is { } p ? Results.Ok(p) : Results.NotFound());
    app.MapGet("/api/ai/history/{id}", (string id) => store.GetMlHistory(id));
}
else
{
    app.MapGet("/api/overview", async (IHealthStatsRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetOverviewAsync(ct)));

    app.MapGet("/api/live", async (IHealthStatsRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetLiveSnapshotAsync(ct)));

    app.MapGet("/api/patients", async (IHealthStatsRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetPatientProfilesAsync(ct)));

    app.MapGet("/api/patients/{id}", async (string id, string? range, IHealthStatsRepository repo, CancellationToken ct) =>
    {
        var detail = await repo.GetPatientDetailAsync(id, range ?? "day", ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    });

    app.MapGet("/api/patients/{id}/profile", async (string id, IHealthStatsRepository repo, CancellationToken ct) =>
    {
        var profile = await repo.GetPatientProfileAsync(id, ct);
        return profile is null ? Results.NotFound() : Results.Ok(profile);
    });

    app.MapGet("/api/alerts", async (string? severity, string? type, string? q, int? limit, IHealthStatsRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetAlertsAsync(severity, type, q, limit ?? 200, ct)));

    app.MapGet("/api/analytics", async (IHealthStatsRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetAnalyticsAsync(ct)));

    app.MapGet("/api/high-risk", async (int? limit, IHealthStatsRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetHighRiskPatientsAsync(limit ?? 8, ct)));

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

    app.MapGet("/api/ai/predictions", (MlPredictionStore store) => Results.Ok(store.Latest()));

    app.MapGet("/api/ai/patient/{id}", async (string id, MlPredictionStore store, IHealthStatsRepository repo,
        HeartAttackPredictionService predictor, CancellationToken ct) =>
    {
        var cached = store.Get(id);
        if (cached is not null)
        {
            return Results.Ok(cached);
        }

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
}

app.MapGet("/api/ai/factors", (HeartAttackPredictionService svc) =>
    svc.Metadata is not null && svc.Metadata.RootElement.TryGetProperty("topFactors", out var f)
        ? Results.Json(f.Clone())
        : Results.Json(Array.Empty<object>()));

app.MapGet("/api/ai/metrics", (HeartAttackPredictionService svc) =>
    svc.Metadata is not null
        ? Results.Json(svc.Metadata.RootElement.Clone())
        : Results.Json(new { chosenModel = svc.ChosenModel, modelLoaded = svc.ModelLoaded }));

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", dataGeneration = dataGenEnabled, timestamp = DateTimeOffset.UtcNow }));

app.MapPost("/api/push/register", (DeviceRegistration body, DeviceTokenStore store) =>
{
    if (string.IsNullOrWhiteSpace(body.Token))
    {
        return Results.BadRequest(new { error = "Token is required." });
    }

    store.Register(body.Token);
    return Results.Ok(new { registered = true, platform = body.Platform ?? "unknown" });
});

app.MapDelete("/api/push/register/{token}", (string token, DeviceTokenStore store) =>
{
    store.Unregister(token);
    return Results.Ok(new { removed = true });
});

app.MapHub<HealthHub>("/healthHub");

app.MapFallbackToFile("index.html");

app.Run();

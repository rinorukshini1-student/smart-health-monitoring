using System.Diagnostics;
using System.Text.Json;
using Vue.Api;
using Vue.Api.Ai;
using Vue.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton(new HealthDataStore(PatientCatalog.Patients));
builder.Services.AddSingleton<HeartAttackPredictionService>();
builder.Services.AddHostedService<DataGeneratorService>();

const string DevCors = "vue-dev";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy =>
    policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseCors(DevCors);

// Serve the built Vue SPA (vue-client/dist copied into wwwroot) when present.
app.UseDefaultFiles();
app.UseStaticFiles();

// Approximate backend API response time for the System Health page.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        var sw = Stopwatch.StartNew();
        await next();
        sw.Stop();
        ctx.RequestServices.GetRequiredService<HealthDataStore>().RecordApiResponse(sw.Elapsed.TotalMilliseconds);
    }
    else
    {
        await next();
    }
});

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

// AI / heart-attack ML
app.MapGet("/api/ai/predictions", () => store.GetMlPredictions());
app.MapGet("/api/ai/patient/{id}", (string id) =>
    store.GetMlPrediction(id) is { } p ? Results.Ok(p) : Results.NotFound());
app.MapGet("/api/ai/history/{id}", (string id) => store.GetMlHistory(id));
app.MapGet("/api/ai/factors", (HeartAttackPredictionService svc) =>
    svc.Metadata is not null && svc.Metadata.RootElement.TryGetProperty("topFactors", out var f)
        ? Results.Json(f.Clone())
        : Results.Json(Array.Empty<object>()));
app.MapGet("/api/ai/metrics", (HeartAttackPredictionService svc) =>
    svc.Metadata is not null
        ? Results.Json(svc.Metadata.RootElement.Clone())
        : Results.Json(new { chosenModel = svc.ChosenModel, modelLoaded = svc.ModelLoaded }));

app.MapHub<HealthHub>("/healthHub");

// SPA fallback for client-side routing (only matters when wwwroot/index.html exists).
app.MapFallbackToFile("index.html");

app.Run();

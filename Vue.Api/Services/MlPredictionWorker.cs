using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Vue.Api.Ai;
using Vue.Api.Data;
using Vue.Api.Hubs;

namespace Vue.Api.Services;

public sealed class MlPredictionWorker : BackgroundService
{
    // Probability at/above which an AI heart-risk alert is raised.
    private const double AlertProbabilityThreshold = 0.70;

    // Minimum gap between AI heart-risk alerts for the same patient (separate from
    // the vital-based SmartAlertEngine cooldown in Streaming.App).
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAiAlertAt = new();

    private readonly IHealthStatsRepository _repository;
    private readonly HeartAttackPredictionService _predictor;
    private readonly MlPredictionStore _store;
    private readonly IHubContext<HealthHub> _hub;
    private readonly ILogger<MlPredictionWorker> _logger;

    public MlPredictionWorker(
        IHealthStatsRepository repository,
        HeartAttackPredictionService predictor,
        MlPredictionStore store,
        IHubContext<HealthHub> hub,
        ILogger<MlPredictionWorker> logger)
    {
        _repository = repository;
        _predictor = predictor;
        _store = store;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var profiles = await _repository.GetPatientProfilesAsync(stoppingToken);
                var live = (await _repository.GetLiveSnapshotAsync(stoppingToken))
                    .ToDictionary(r => r.PatientId);

                foreach (var profile in profiles)
                {
                    live.TryGetValue(profile.PatientId, out var vitals);
                    var prediction = _predictor.Predict(profile, vitals);

                    _store.Update(prediction);
                    await _repository.StoreMlPredictionAsync(prediction, stoppingToken);
                    await _hub.Clients.All.SendAsync("mlPredictionReceived", prediction, stoppingToken);

                    await RaiseAiAlertIfNeededAsync(prediction, vitals, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ML prediction cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    // Emits a high-risk AI alert (type "AI_HEART_RISK") when the predicted probability
    // crosses the threshold, applying a 5-minute per-patient cooldown so the same
    // alert is not raised repeatedly. Persisted to alerts_log and pushed via SignalR.
    private async Task RaiseAiAlertIfNeededAsync(MlPredictionDto prediction, LivePatientRow? vitals, CancellationToken ct)
    {
        if (prediction.RiskProbability < AlertProbabilityThreshold)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastAiAlertAt.TryGetValue(prediction.PatientId, out var last) && now - last < AlertCooldown)
        {
            return; // still within cooldown window
        }

        _lastAiAlertAt[prediction.PatientId] = now;

        var probabilityPercent = Math.Round(prediction.RiskProbability * 100, 1);
        var topFactor = prediction.TopFactors.FirstOrDefault();
        var message = topFactor is null
            ? $"Rrezik i lartë i infarktit ({probabilityPercent}%) parashikuar nga modeli AI."
            : $"Rrezik i lartë i infarktit ({probabilityPercent}%) parashikuar nga modeli AI · {topFactor}.";

        var alert = new AlertMessageDto(
            Guid.NewGuid(),
            prediction.PatientId,
            prediction.RoomNumber,
            "AI_HEART_RISK",
            "CRITICAL",
            message,
            probabilityPercent,
            vitals?.HeartRate ?? prediction.HeartRate,
            vitals?.Spo2 ?? 0,
            vitals?.Temperature ?? 0,
            vitals?.SystolicBp ?? prediction.SystolicBp,
            vitals?.DiastolicBp ?? prediction.DiastolicBp,
            vitals?.RespiratoryRate ?? 0,
            now);

        await _repository.StoreAlertAsync(alert, ct);
        await _hub.Clients.All.SendAsync("alertReceived", alert, ct);

        _logger.LogInformation("AI heart-risk alert raised for {PatientId} ({Percent}%).",
            prediction.PatientId, probabilityPercent);
    }
}

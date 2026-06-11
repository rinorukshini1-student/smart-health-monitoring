using Microsoft.AspNetCore.SignalR;
using Vue.Api.Ai;
using Vue.Api.Hubs;
using Vue.Api.Services;

namespace Vue.Api;

// Auto-starting hosted service: generates patient telemetry the moment the API starts,
// runs alerts + AI risk + windowing + the heart-attack ML model, and pushes everything via SignalR.
public sealed class DataGeneratorService : BackgroundService
{
    private readonly HealthDataStore _store;
    private readonly HeartAttackPredictionService _predictor;
    private readonly IHubContext<HealthHub> _hub;
    private readonly FirebasePushService _push;
    private readonly SmartAlertEngine _alertEngine;
    private readonly ILogger<DataGeneratorService> _logger;
    private readonly Random _random = new();

    public DataGeneratorService(HealthDataStore store, HeartAttackPredictionService predictor, IHubContext<HealthHub> hub, FirebasePushService push, SmartAlertEngine alertEngine, ILogger<DataGeneratorService> logger)
    {
        _store = store;
        _predictor = predictor;
        _hub = hub;
        _push = push;
        _alertEngine = alertEngine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data generator started - producing live patient telemetry.");
        Backfill();

        var tick = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = 0;
                foreach (var patient in _store.Patients)
                {
                    var reading = CreateReading(patient, DateTimeOffset.UtcNow);
                    _store.AddReading(reading);
                    await _hub.Clients.All.SendAsync("vitalsReceived", reading, stoppingToken);

                    var risk = RiskScoringEngine.Assess(reading, patient.Age);
                    _store.AddRisk(risk);
                    await _hub.Clients.All.SendAsync("riskReceived", risk, stoppingToken);

                    foreach (var alert in _alertEngine.Evaluate(reading))
                    {
                        _store.AddAlert(alert);
                        await _hub.Clients.All.SendAsync("alertReceived", alert, stoppingToken);
                        await _push.NotifyAlertAsync(alert, stoppingToken);
                    }
                    count++;
                }
                _store.RecordBatch(count);

                // Metrics every tick (~2s).
                await _hub.Clients.All.SendAsync("metricsReceived", _store.GetStreamMetrics(), stoppingToken);

                // Heart-attack ML predictions every ~14s.
                if (tick % 7 == 0)
                {
                    var live = _store.GetLiveSnapshot().ToDictionary(r => r.PatientId);
                    foreach (var profile in _store.GetProfiles())
                    {
                        live.TryGetValue(profile.PatientId, out var row);
                        var vitals = row is null ? null : ToReading(row);
                        var prediction = _predictor.Predict(profile, vitals);
                        _store.AddMlPrediction(prediction);
                        await _hub.Clients.All.SendAsync("mlPredictionReceived", prediction, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Generation tick failed."); }

            tick++;
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    // Seed ~20 minutes of history so charts are populated immediately on startup.
    private void Backfill()
    {
        var now = DateTimeOffset.UtcNow;
        for (var minutesAgo = 20; minutesAgo >= 1; minutesAgo--)
        {
            var ts = now.AddMinutes(-minutesAgo);
            foreach (var patient in _store.Patients)
            {
                var reading = CreateReading(patient, ts);
                _store.AddReading(reading);
                _store.AddRisk(RiskScoringEngine.Assess(reading, patient.Age));
                foreach (var alert in _alertEngine.Evaluate(reading)) _store.AddAlert(alert);
            }
        }

        // Seed an initial ML prediction per patient.
        var live = _store.GetLiveSnapshot().ToDictionary(r => r.PatientId);
        foreach (var profile in _store.GetProfiles())
        {
            live.TryGetValue(profile.PatientId, out var row);
            _store.AddMlPrediction(_predictor.Predict(profile, row is null ? null : ToReading(row)));
        }
        _logger.LogInformation("Backfilled startup history for {Count} patients.", _store.Patients.Count);
    }

    private VitalReadingDto CreateReading(PatientDefinition p, DateTimeOffset at)
    {
        var heartSpike = _random.NextDouble() < 0.08;
        var oxygenDrop = _random.NextDouble() < 0.06;
        var feverSpike = _random.NextDouble() < 0.07;
        var hypertension = _random.NextDouble() < 0.07;
        var tachypnea = _random.NextDouble() < 0.05;

        var heartRate = heartSpike ? (_random.NextDouble() < 0.5 ? _random.Next(121, 165) : _random.Next(38, 50)) : _random.Next(60, 101);
        var spo2 = oxygenDrop ? _random.Next(85, 92) : _random.Next(95, 101);
        var temperature = feverSpike ? Math.Round(38.6 + _random.NextDouble() * 1.4, 1) : Math.Round(36.4 + _random.NextDouble() * 1.4, 1);
        var systolic = hypertension ? _random.Next(141, 181) : _random.Next(105, 131);
        var diastolic = hypertension ? _random.Next(91, 111) : _random.Next(65, 86);
        var respiratoryRate = tachypnea ? (_random.NextDouble() < 0.5 ? _random.Next(25, 33) : _random.Next(6, 11)) : _random.Next(12, 19);

        return new VitalReadingDto(p.PatientId, p.PatientName, p.RoomNumber, p.Age,
            heartRate, spo2, temperature, systolic, diastolic, respiratoryRate, at);
    }

    private static VitalReadingDto ToReading(LivePatientRow r) =>
        new(r.PatientId, r.PatientName, r.RoomNumber, r.Age, r.HeartRate, r.Spo2, r.Temperature,
            r.SystolicBp, r.DiastolicBp, r.RespiratoryRate, r.LastUpdate);
}

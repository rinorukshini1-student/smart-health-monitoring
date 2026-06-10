using Microsoft.AspNetCore.SignalR;
using Vue.Api.Ai;
using Vue.Api.Data;
using Vue.Api.Hubs;

namespace Vue.Api.Services;

public sealed class MlPredictionWorker : BackgroundService
{
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ML prediction cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}

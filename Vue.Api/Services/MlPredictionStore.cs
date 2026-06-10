using System.Collections.Concurrent;

namespace Vue.Api.Services;

public sealed class MlPredictionStore
{
    private readonly ConcurrentDictionary<string, MlPredictionDto> _latest = new();

    public void Update(MlPredictionDto prediction) => _latest[prediction.PatientId] = prediction;

    public IReadOnlyList<MlPredictionDto> Latest() =>
        _latest.Values.OrderByDescending(p => p.RiskProbability).ToArray();

    public MlPredictionDto? Get(string patientId) =>
        _latest.TryGetValue(patientId, out var p) ? p : null;
}

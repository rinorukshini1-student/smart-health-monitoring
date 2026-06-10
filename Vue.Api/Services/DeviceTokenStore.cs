namespace Vue.Api.Services;

public sealed class FirebaseOptions
{
    public bool Enabled { get; init; }
    public string ServiceAccountPath { get; init; } = "firebase-service-account.json";
    public string ProjectId { get; init; } = string.Empty;
}

public sealed record DeviceRegistration(string Token, string Platform);

public sealed class DeviceTokenStore
{
    private readonly HashSet<string> _tokens = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public void Register(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (_lock) { _tokens.Add(token.Trim()); }
    }

    public void Unregister(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (_lock) { _tokens.Remove(token.Trim()); }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock) { return _tokens.ToArray(); }
    }
}

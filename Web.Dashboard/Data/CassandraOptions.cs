namespace Web.Dashboard.Data;

public sealed record CassandraOptions
{
    public string ContactPoint { get; init; } = "localhost";
    public int Port { get; init; } = 9042;
    public string Keyspace { get; init; } = "smart_health";
}

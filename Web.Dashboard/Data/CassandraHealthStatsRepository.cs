using Cassandra;
using Microsoft.Extensions.Options;
using Web.Dashboard.Models;

namespace Web.Dashboard.Data;

public sealed class CassandraHealthStatsRepository : IHealthStatsRepository, IAsyncDisposable
{
    private static readonly string[] Rooms = Enumerable.Range(101, 10).Select(room => room.ToString()).ToArray();
    private readonly ICluster _cluster;
    private readonly Lazy<Task<Cassandra.ISession>> _session;
    private readonly CassandraOptions _options;
    private readonly ILogger<CassandraHealthStatsRepository> _logger;

    public CassandraHealthStatsRepository(IOptions<CassandraOptions> options, ILogger<CassandraHealthStatsRepository> logger)
    {
        _logger = logger;
        _options = options.Value;
        _cluster = Cluster.Builder()
            .AddContactPoint(_options.ContactPoint)
            .WithPort(_options.Port)
            .Build();

        _session = new Lazy<Task<Cassandra.ISession>>(InitializeSessionAsync);
    }

    private async Task<Cassandra.ISession> InitializeSessionAsync()
    {
        var systemSession = await _cluster.ConnectAsync();
        await systemSession.ExecuteAsync(new SimpleStatement(
            $"CREATE KEYSPACE IF NOT EXISTS {_options.Keyspace} WITH replication = {{ 'class': 'SimpleStrategy', 'replication_factor': 1 }}"));

        var session = await _cluster.ConnectAsync(_options.Keyspace);
        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS patient_vitals (
              room_number text,
              vital_day date,
              recorded_at timestamp,
              patient_id text,
              patient_name text,
              heart_rate int,
              spo2 int,
              temperature double,
              PRIMARY KEY ((room_number, vital_day), recorded_at, patient_id)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC, patient_id ASC)
            """));

        await session.ExecuteAsync(new SimpleStatement("""
            CREATE TABLE IF NOT EXISTS alerts_log (
              room_number text,
              alert_day date,
              recorded_at timestamp,
              alert_id uuid,
              patient_id text,
              severity text,
              message text,
              heart_rate int,
              spo2 int,
              temperature double,
              PRIMARY KEY ((room_number, alert_day), recorded_at, alert_id)
            ) WITH CLUSTERING ORDER BY (recorded_at DESC, alert_id ASC)
            """));

        await systemSession.ShutdownAsync();
        return session;
    }

    public async Task<HealthStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await _session.Value;
            var since = DateTimeOffset.UtcNow.AddHours(-24);
            var days = new[] { ToCassandraDate(since), ToCassandraDate(DateTimeOffset.UtcNow) }.Distinct().ToArray();
            var heartRates = new List<HeartRatePoint>();
            var temperatures = new List<double>();
            var alertCounts = new List<RoomAlertCount>();

            foreach (var room in Rooms)
            {
                var roomAlertCount = 0;

                foreach (var day in days)
                {
                    var vitals = await session.ExecuteAsync(new SimpleStatement(
                        """
                        SELECT recorded_at, heart_rate, temperature
                        FROM patient_vitals
                        WHERE room_number = ? AND vital_day = ?
                        """,
                        room,
                        day));

                    foreach (var row in vitals)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var recordedAt = new DateTimeOffset(row.GetValue<DateTime>("recorded_at"), TimeSpan.Zero);
                        if (recordedAt < since)
                        {
                            continue;
                        }

                        heartRates.Add(new HeartRatePoint(
                            recordedAt,
                            row.GetValue<int>("heart_rate"),
                            room));
                        temperatures.Add(row.GetValue<double>("temperature"));
                    }

                    var alerts = await session.ExecuteAsync(new SimpleStatement(
                        """
                        SELECT alert_id
                        FROM alerts_log
                        WHERE room_number = ? AND alert_day = ?
                        """,
                        room,
                        day));

                    roomAlertCount += alerts.Count();
                }

                alertCounts.Add(new RoomAlertCount(room, roomAlertCount));
            }

            return new HealthStatistics(
                heartRates.OrderBy(point => point.Timestamp).TakeLast(240).ToArray(),
                alertCounts,
                temperatures.Count == 0 ? 0 : Math.Round(temperatures.Average(), 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load Cassandra statistics. Returning empty dashboard data.");
            return new HealthStatistics(
                Array.Empty<HeartRatePoint>(),
                Rooms.Select(room => new RoomAlertCount(room, 0)).ToArray(),
                0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session.IsValueCreated)
        {
            var session = await _session.Value;
            await session.ShutdownAsync();
        }

        _cluster.Dispose();
    }

    private static LocalDate ToCassandraDate(DateTimeOffset timestamp)
    {
        var date = timestamp.UtcDateTime.Date;
        return new LocalDate(date.Year, date.Month, date.Day);
    }
}

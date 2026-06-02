using Cassandra;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Spark.Sql;
using System.Text.Json;
using static Microsoft.Spark.Sql.Functions;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var kafka = configuration.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();
var cassandraOptions = configuration.GetSection("Cassandra").Get<CassandraOptions>() ?? new CassandraOptions();
var signalROptions = configuration.GetSection("SignalR").Get<SignalROptions>() ?? new SignalROptions();

await using var healthRepository = await CassandraHealthRepository.CreateAsync(cassandraOptions);
await using var hubConnection = new HubConnectionBuilder()
    .WithUrl(signalROptions.HubUrl)
    .WithAutomaticReconnect()
    .Build();

await StartSignalRWithRetryAsync(hubConnection, signalROptions.HubUrl);
Console.WriteLine($"Connected to SignalR hub: {signalROptions.HubUrl}");

if (!args.Contains("--spark", StringComparer.OrdinalIgnoreCase))
{
    await RunDirectKafkaModeAsync(kafka, hubConnection, healthRepository);
    return;
}

Console.WriteLine("Spark mode selected. Start this process with spark-submit so the Microsoft.Spark JVM bridge is available.");
var spark = SparkSession
    .Builder()
    .AppName("Smart Health Monitoring Streaming")
    .GetOrCreate();

var schema = """
    patientId STRING,
    patientName STRING,
    roomNumber STRING,
    heartRate INT,
    spo2 INT,
    temperature DOUBLE,
    recordedAt TIMESTAMP
    """;

var rawKafka = spark
    .ReadStream()
    .Format("kafka")
    .Option("kafka.bootstrap.servers", kafka.BootstrapServers)
    .Option("subscribe", kafka.Topic)
    .Option("startingOffsets", "latest")
    .Load();

var vitals = rawKafka
    .Select(FromJson(Col("value").Cast("string"), schema).Alias("data"))
    .Select("data.*");

var query = vitals
    .WriteStream()
    .ForeachBatch((batch, batchId) =>
    {
        ProcessBatchAsync(batch, batchId, hubConnection, healthRepository).GetAwaiter().GetResult();
    })
    .Start();

query.AwaitTermination();

static async Task RunDirectKafkaModeAsync(
    KafkaOptions kafka,
    HubConnection hubConnection,
    CassandraHealthRepository healthRepository)
{
    var config = new ConsumerConfig
    {
        BootstrapServers = kafka.BootstrapServers,
        GroupId = "smart-health-direct-streaming",
        AutoOffsetReset = AutoOffsetReset.Latest,
        EnableAutoCommit = true
    };

    using var consumer = new ConsumerBuilder<string, string>(config).Build();
    consumer.Subscribe(kafka.Topic);
    Console.WriteLine($"Direct Kafka mode is consuming topic {kafka.Topic} from {kafka.BootstrapServers}");

    while (true)
    {
        var result = consumer.Consume();
        var reading = JsonSerializer.Deserialize<VitalReading>(result.Message.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (reading is null)
        {
            continue;
        }

        await ProcessReadingAsync(reading, hubConnection, healthRepository, "Direct");
    }
}

static async Task StartSignalRWithRetryAsync(HubConnection hubConnection, string hubUrl)
{
    var attempt = 1;

    while (true)
    {
        try
        {
            await hubConnection.StartAsync();
            return;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"SignalR hub is not reachable at {hubUrl}. Retry {attempt++} in 5 seconds. {ex.Message}");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

static async Task ProcessBatchAsync(
    DataFrame batch,
    long batchId,
    HubConnection hubConnection,
    CassandraHealthRepository healthRepository)
{
    foreach (var row in batch.Collect())
    {
        var reading = VitalReading.From(row);
        await ProcessReadingAsync(reading, hubConnection, healthRepository, $"Batch {batchId}");
    }
}

static async Task ProcessReadingAsync(
    VitalReading reading,
    HubConnection hubConnection,
    CassandraHealthRepository healthRepository,
    string source)
{
    await healthRepository.InsertVitalAsync(reading);
    await hubConnection.InvokeAsync("PublishVitals", reading);
    Console.WriteLine(
        $"Vitals sent: room {reading.RoomNumber}, HR {reading.HeartRate}, SpO2 {reading.Spo2}, Temp {reading.Temperature:0.0}C");

    var alert = AlertMessage.FromCriticalReading(reading);
    if (alert is null)
    {
        return;
    }

    await healthRepository.InsertAlertAsync(alert);
    await hubConnection.InvokeAsync("PublishAlert", alert);
    Console.WriteLine($"{source}: emergency in room {alert.RoomNumber}: {alert.Message}");
}

public sealed record KafkaOptions
{
    public string BootstrapServers { get; init; } = "178.105.181.143:9092";
    public string Topic { get; init; } = "health-vitals";
}

public sealed record CassandraOptions
{
    public string ContactPoint { get; init; } = "178.105.181.143";
    public int Port { get; init; } = 9042;
    public string Keyspace { get; init; } = "smart_health";
}

public sealed record SignalROptions
{
    public string HubUrl { get; init; } = "http://localhost:5084/healthHub";
}

public sealed record VitalReading(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int HeartRate,
    int Spo2,
    double Temperature,
    DateTimeOffset RecordedAt)
{
    public static VitalReading From(Microsoft.Spark.Sql.Row row)
    {
        var recordedAt = row.GetAs<DateTime>("recordedAt");
        return new VitalReading(
            row.GetAs<string>("patientId"),
            row.GetAs<string>("patientName"),
            row.GetAs<string>("roomNumber"),
            row.GetAs<int>("heartRate"),
            row.GetAs<int>("spo2"),
            row.GetAs<double>("temperature"),
            new DateTimeOffset(DateTime.SpecifyKind(recordedAt, DateTimeKind.Utc)));
    }
}

public sealed record AlertMessage(
    Guid AlertId,
    string PatientId,
    string RoomNumber,
    string Severity,
    string Message,
    int HeartRate,
    int Spo2,
    double Temperature,
    DateTimeOffset RecordedAt)
{
    public static AlertMessage? FromCriticalReading(VitalReading reading)
    {
        var reasons = new List<string>();

        if (reading.HeartRate > 120)
        {
            reasons.Add($"HR {reading.HeartRate} BPM");
        }

        if (reading.Spo2 < 92)
        {
            reasons.Add($"SpO2 {reading.Spo2}%");
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        return new AlertMessage(
            Guid.NewGuid(),
            reading.PatientId,
            reading.RoomNumber,
            "CRITICAL",
            $"EMERGENCY: {string.Join(", ", reasons)}",
            reading.HeartRate,
            reading.Spo2,
            reading.Temperature,
            reading.RecordedAt);
    }
}

public sealed class CassandraHealthRepository : IAsyncDisposable
{
    private readonly ICluster _cluster;
    private readonly Cassandra.ISession _session;
    private readonly PreparedStatement _insertVital;
    private readonly PreparedStatement _insertAlert;

    private CassandraHealthRepository(
        ICluster cluster,
        Cassandra.ISession session,
        PreparedStatement insertVital,
        PreparedStatement insertAlert)
    {
        _cluster = cluster;
        _session = session;
        _insertVital = insertVital;
        _insertAlert = insertAlert;
    }

    public static async Task<CassandraHealthRepository> CreateAsync(CassandraOptions options)
    {
        var cluster = Cluster.Builder()
            .AddContactPoint(options.ContactPoint)
            .WithPort(options.Port)
            .Build();

        var systemSession = await cluster.ConnectAsync();
        await systemSession.ExecuteAsync(new SimpleStatement(
            $"CREATE KEYSPACE IF NOT EXISTS {options.Keyspace} WITH replication = {{ 'class': 'SimpleStrategy', 'replication_factor': 1 }}"));
        await systemSession.ShutdownAsync();

        var session = await cluster.ConnectAsync(options.Keyspace);
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

        var insertVital = await session.PrepareAsync("""
            INSERT INTO patient_vitals
            (room_number, vital_day, recorded_at, patient_id, patient_name, heart_rate, spo2, temperature)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """);

        var insertAlert = await session.PrepareAsync("""
            INSERT INTO alerts_log
            (room_number, alert_day, recorded_at, alert_id, patient_id, severity, message, heart_rate, spo2, temperature)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """);

        return new CassandraHealthRepository(cluster, session, insertVital, insertAlert);
    }

    public async Task InsertVitalAsync(VitalReading reading)
    {
        var vitalDay = ToCassandraDate(reading.RecordedAt);
        var statement = _insertVital.Bind(
            reading.RoomNumber,
            vitalDay,
            reading.RecordedAt.UtcDateTime,
            reading.PatientId,
            reading.PatientName,
            reading.HeartRate,
            reading.Spo2,
            reading.Temperature);

        await _session.ExecuteAsync(statement);
    }

    public async Task InsertAlertAsync(AlertMessage alert)
    {
        var alertDay = ToCassandraDate(alert.RecordedAt);
        var statement = _insertAlert.Bind(
            alert.RoomNumber,
            alertDay,
            alert.RecordedAt.UtcDateTime,
            alert.AlertId,
            alert.PatientId,
            alert.Severity,
            alert.Message,
            alert.HeartRate,
            alert.Spo2,
            alert.Temperature);

        await _session.ExecuteAsync(statement);
    }

    private static LocalDate ToCassandraDate(DateTimeOffset timestamp)
    {
        var date = timestamp.UtcDateTime.Date;
        return new LocalDate(date.Year, date.Month, date.Day);
    }

    public async ValueTask DisposeAsync()
    {
        await _session.ShutdownAsync();
        _cluster.Dispose();
    }
}

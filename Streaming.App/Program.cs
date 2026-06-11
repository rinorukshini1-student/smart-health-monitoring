using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Spark.Sql;
using System.Diagnostics;
using System.Text.Json;
using static Microsoft.Spark.Sql.Functions;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var kafka = configuration.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();
var cassandraOptions = configuration.GetSection("Cassandra").Get<CassandraOptions>() ?? new CassandraOptions();
var signalROptions = configuration.GetSection("SignalR").Get<SignalROptions>() ?? new SignalROptions();

// Spark needs a durable checkpoint dir to track Kafka offsets across restarts (exactly-once semantics).
var checkpointRoot = configuration["Spark:CheckpointDir"] ?? "/tmp/spark-checkpoints";

await using var healthRepository = await CassandraHealthRepository.CreateAsync(cassandraOptions);
await using var hubConnection = new HubConnectionBuilder()
    .WithUrl(signalROptions.HubUrl)
    .WithAutomaticReconnect()
    .Build();

await StartSignalRWithRetryAsync(hubConnection, signalROptions.HubUrl);
Console.WriteLine($"Connected to SignalR hub: {signalROptions.HubUrl}");

// Shared processing context (window aggregator + live metrics + smart alerting) for both run modes.
var aggregator = new WindowAggregator();
var metrics = new MetricsCollector();
var alertEngine = new SmartAlertEngine();

// Periodically push a metrics snapshot to the System Health dashboard page.
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        try
        {
            await hubConnection.InvokeAsync("PublishMetrics", metrics.Snapshot());
        }
        catch
        {
            // Hub may be temporarily unavailable; metrics are best-effort.
        }
    }
});

if (!args.Contains("--spark", StringComparer.OrdinalIgnoreCase))
{
    await RunDirectKafkaModeAsync(kafka, hubConnection, healthRepository, aggregator, metrics, alertEngine);
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
    age INT,
    heartRate INT,
    spo2 INT,
    temperature DOUBLE,
    systolicBp INT,
    diastolicBp INT,
    respiratoryRate INT,
    recordedAt TIMESTAMP
    """;

var rawKafka = spark
    .ReadStream()
    .Format("kafka")
    .Option("kafka.bootstrap.servers", kafka.BootstrapServers)
    .Option("subscribe", kafka.Topic)
    .Option("startingOffsets", "latest")
    // After Kafka restarts, checkpointed offsets may no longer exist — keep streaming alive.
    .Option("failOnDataLoss", "false")
    .Load();

var vitals = rawKafka
    .Select(FromJson(Col("value").Cast("string"), schema).Alias("data"))
    .Select("data.*")
    // Validation/filtering stage: drop malformed or physiologically impossible readings.
    .Filter("patientId IS NOT NULL AND heartRate > 0 AND heartRate < 260 AND spo2 > 0 AND spo2 <= 100");

// Per-reading processing: persistence, alerts and AI risk scoring.
var perReadingQuery = vitals
    .WriteStream()
    .Option("checkpointLocation", $"{checkpointRoot}/per-reading")
    .ForeachBatch((batch, batchId) =>
    {
        ProcessBatchAsync(batch, batchId, hubConnection, healthRepository, aggregator, metrics, alertEngine).GetAwaiter().GetResult();
    })
    .Start();

// Spark sliding-window aggregation (5-minute window, 1-minute slide) stored in Cassandra.
var windowed = vitals
    .WithWatermark("recordedAt", "10 minutes")
    .GroupBy(Window(Col("recordedAt"), "5 minutes", "1 minute"), Col("roomNumber"), Col("patientId"))
    .Agg(
        Avg("heartRate").Alias("avg_heart_rate"),
        Avg("spo2").Alias("avg_spo2"),
        Avg("temperature").Alias("avg_temperature"),
        Avg("systolicBp").Alias("avg_systolic"),
        Avg("diastolicBp").Alias("avg_diastolic"),
        Avg("respiratoryRate").Alias("avg_respiratory"),
        Count(Lit(1)).Alias("sample_count"));

var windowQuery = windowed
    .WriteStream()
    .OutputMode("update")
    .Option("checkpointLocation", $"{checkpointRoot}/window")
    .ForeachBatch((batch, batchId) =>
    {
        StoreWindowBatchAsync(batch, healthRepository, metrics).GetAwaiter().GetResult();
    })
    .Start();

perReadingQuery.AwaitTermination();
windowQuery.AwaitTermination();

static async Task RunDirectKafkaModeAsync(
    KafkaOptions kafka,
    HubConnection hubConnection,
    CassandraHealthRepository healthRepository,
    WindowAggregator aggregator,
    MetricsCollector metrics,
    SmartAlertEngine alertEngine)
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

        if (reading is null || !IsValid(reading))
        {
            continue;
        }

        await ProcessReadingAsync(reading, hubConnection, healthRepository, aggregator, metrics, alertEngine, "Direct");
    }
}

// Basic validation/filtering stage (matches the Spark .Filter above).
static bool IsValid(VitalReading r) =>
    !string.IsNullOrWhiteSpace(r.PatientId)
    && r.HeartRate is > 0 and < 260
    && r.Spo2 is > 0 and <= 100;

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
    CassandraHealthRepository healthRepository,
    WindowAggregator aggregator,
    MetricsCollector metrics,
    SmartAlertEngine alertEngine)
{
    var stopwatch = Stopwatch.StartNew();
    var count = 0;

    foreach (var row in batch.Collect())
    {
        var reading = VitalReading.From(row);
        await ProcessReadingAsync(reading, hubConnection, healthRepository, aggregator, metrics, alertEngine, $"Batch {batchId}");
        count++;
    }

    stopwatch.Stop();
    metrics.RecordBatch(count, stopwatch.Elapsed.TotalMilliseconds);
}

static async Task StoreWindowBatchAsync(DataFrame batch, CassandraHealthRepository healthRepository, MetricsCollector metrics)
{
    foreach (var row in batch.Collect())
    {
        var window = row.GetAs<Row>("window");
        var windowStart = SparkRowReader.ReadTimestamp(window, "start");

        var aggregate = new WindowAggregate(
            row.GetAs<string>("patientId"),
            row.GetAs<string>("roomNumber"),
            windowStart,
            row.GetAs<double>("avg_heart_rate"),
            row.GetAs<double>("avg_spo2"),
            row.GetAs<double>("avg_temperature"),
            row.GetAs<double>("avg_systolic"),
            row.GetAs<double>("avg_diastolic"),
            row.GetAs<double>("avg_respiratory"),
            (int)row.GetAs<long>("sample_count"));

        await healthRepository.InsertWindowAsync(aggregate);
        metrics.RecordWindow();
    }
}

static async Task ProcessReadingAsync(
    VitalReading reading,
    HubConnection hubConnection,
    CassandraHealthRepository healthRepository,
    WindowAggregator aggregator,
    MetricsCollector metrics,
    SmartAlertEngine alertEngine,
    string source)
{
    // 1. Persist the raw reading and keep the patient registry up to date.
    await healthRepository.InsertVitalAsync(reading);
    await healthRepository.UpsertPatientAsync(reading);
    await hubConnection.InvokeAsync("PublishVitals", reading);
    metrics.RecordMessage();

    // 2. Sliding-window aggregation (direct-mode path).
    var window = aggregator.Add(reading);
    await healthRepository.InsertWindowAsync(window);
    metrics.RecordWindow();

    // 3. AI risk scoring.
    var risk = RiskScoringEngine.Assess(reading);
    await healthRepository.InsertRiskAsync(risk);
    await hubConnection.InvokeAsync("PublishRisk", risk);

    // 4. Smart alert evaluation - sustained-breach + cooldown + stabilization (no per-reading spam).
    var alerts = alertEngine.Evaluate(reading);
    foreach (var alert in alerts)
    {
        await healthRepository.InsertAlertAsync(alert);
        await hubConnection.InvokeAsync("PublishAlert", alert);
        metrics.RecordAlert();
        Console.WriteLine($"{source}: {alert.Severity} {alert.AlertType} for {alert.PatientId} - {alert.Message}");
    }

    Console.WriteLine(
        $"Vitals stored: room {reading.RoomNumber}, HR {reading.HeartRate}, SpO2 {reading.Spo2}, Temp {reading.Temperature:0.0}C, " +
        $"BP {reading.SystolicBp}/{reading.DiastolicBp}, RR {reading.RespiratoryRate} | Risk {risk.Score} ({risk.Category})");
}

namespace Simulator.App;

using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly KafkaOptions _kafkaOptions;
    private readonly Random _random = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PatientSensor[] _patients =
    [
        new("P001", "Patient 01", "101"),
        new("P002", "Patient 02", "102"),
        new("P003", "Patient 03", "103"),
        new("P004", "Patient 04", "104"),
        new("P005", "Patient 05", "105"),
        new("P006", "Patient 06", "106"),
        new("P007", "Patient 07", "107"),
        new("P008", "Patient 08", "108"),
        new("P009", "Patient 09", "109"),
        new("P010", "Patient 10", "110")
    ];

    public Worker(ILogger<Worker> logger, IOptions<KafkaOptions> kafkaOptions)
    {
        _logger = logger;
        _kafkaOptions = kafkaOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            ClientId = "smart-health-simulator",
            Acks = Acks.Leader
        };

        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
        _logger.LogInformation("Publishing simulated vitals to Kafka topic {Topic}", _kafkaOptions.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var patient in _patients)
            {
                var reading = CreateReading(patient);
                var json = JsonSerializer.Serialize(reading, _jsonOptions);

                await producer.ProduceAsync(
                    _kafkaOptions.Topic,
                    new Message<string, string> { Key = reading.PatientId, Value = json },
                    stoppingToken);

                _logger.LogInformation(
                    "Room {Room}: HR {HeartRate} BPM, SpO2 {Spo2}%, Temp {Temperature}C",
                    reading.RoomNumber,
                    reading.HeartRate,
                    reading.Spo2,
                    reading.Temperature);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private VitalReading CreateReading(PatientSensor patient)
    {
        var spike = _random.NextDouble() < 0.08;
        var oxygenDrop = _random.NextDouble() < 0.06;

        return new VitalReading(
            PatientId: patient.PatientId,
            PatientName: patient.PatientName,
            RoomNumber: patient.RoomNumber,
            HeartRate: spike ? _random.Next(121, 161) : _random.Next(60, 101),
            Spo2: oxygenDrop ? _random.Next(88, 92) : _random.Next(95, 101),
            Temperature: Math.Round(36.5 + (_random.NextDouble() * 3.5), 1),
            RecordedAt: DateTimeOffset.UtcNow);
    }
}

public sealed record KafkaOptions
{
    public string BootstrapServers { get; init; } = "localhost:9092";
    public string Topic { get; init; } = "health-vitals";
}

public sealed record PatientSensor(string PatientId, string PatientName, string RoomNumber);

public sealed record VitalReading(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int HeartRate,
    int Spo2,
    double Temperature,
    DateTimeOffset RecordedAt);

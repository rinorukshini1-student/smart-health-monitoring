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
    private readonly Dictionary<string, VitalsEpisodeState> _episodeStates = new();

    // Each patient carries a fixed clinical profile aligned with the heart-attack dataset features
    // (Age, Sex, Cholesterol, Diabetes, ... Sleep). Vitals (HR/BP/Temp/SpO2/RR) stay dynamic.
    // Profiles are deliberately varied so the AI model produces a spread of low/medium/high risk.
    private readonly PatientSensor[] _patients =
    [
        //new("P001", "Patient 01", "101", 67, "Male",   238, 1, 1, 1, 1, 1, 3.2,  "Unhealthy", 1, 1, 8, 9.1, 31.3, 286, 1, 6),
        //new("P002", "Patient 02", "102", 54, "Male",   190, 0, 0, 1, 0, 1, 5.1,  "Average",   0, 0, 5, 6.0, 27.2, 180, 3, 7),
        //new("P003", "Patient 03", "103", 72, "Female", 265, 1, 1, 0, 1, 0, 1.4,  "Unhealthy", 1, 1, 9, 10.2, 33.8, 320, 0, 5),
        //new("P004", "Patient 04", "104", 45, "Female", 175, 0, 0, 0, 0, 0, 7.8,  "Healthy",   0, 0, 3, 3.5, 23.1, 120, 5, 8),
        //new("P005", "Patient 05", "105", 81, "Male",   290, 1, 1, 1, 1, 1, 0.8,  "Unhealthy", 1, 1, 10, 11.0, 35.6, 410, 0, 4),
        //new("P006", "Patient 06", "106", 38, "Male",   168, 0, 0, 0, 0, 0, 8.5,  "Healthy",   0, 0, 2, 2.8, 22.0, 110, 6, 8),
        //new("P007", "Patient 07", "107", 60, "Female", 220, 1, 0, 1, 1, 1, 2.6,  "Average",   0, 1, 7, 7.4, 29.9, 240, 2, 6),
        //new("P008", "Patient 08", "108", 29, "Female", 160, 0, 0, 0, 0, 0, 9.0,  "Healthy",   0, 0, 1, 2.0, 21.5, 95,  6, 8),
        //new("P009", "Patient 09", "109", 75, "Male",   272, 1, 1, 1, 0, 1, 1.1,  "Unhealthy", 1, 1, 9, 9.8, 34.2, 360, 1, 5),
        //new("P010", "Patient 10", "110", 50, "Female", 205, 0, 1, 0, 1, 0, 4.4,  "Average",   1, 0, 6, 5.5, 28.4, 210, 3, 7)
        new("P001", "Arben Krasniqi", "101", 67, "Male",   238, 1, 1, 1, 1, 1, 3.2,  "Unhealthy", 1, 1, 8, 9.1, 31.3, 286, 1, 6, VitalsBehavior.Normal),
        new("P002", "Ilir Gashi", "102", 54, "Male",   190, 0, 0, 1, 0, 1, 5.1,  "Average",   0, 0, 5, 6.0, 27.2, 180, 3, 7, VitalsBehavior.Normal),
        new("P003", "Arta Berisha", "103", 72, "Female", 265, 1, 1, 0, 1, 0, 1.4,  "Unhealthy", 1, 1, 9, 10.2, 33.8, 320, 0, 5, VitalsBehavior.Normal),
        new("P004", "Elira Hoxha", "104", 45, "Female", 175, 0, 0, 0, 0, 0, 7.8,  "Healthy",   0, 0, 3, 3.5, 23.1, 120, 5, 8, VitalsBehavior.Normal),
        // Bujar: shpesh në gjendje kritike (episode të gjata me vlera të shumta jashtë normës).
        new("P005", "Bujar Shala", "105", 81, "Male",   290, 1, 1, 1, 1, 1, 0.8,  "Unhealthy", 1, 1, 10, 11.0, 35.6, 410, 0, 4, VitalsBehavior.CriticalProne),
        // Dardan: stabil për periudha të gjata, me devijime të rralla dhe të lehta.
        new("P006", "Dardan Morina", "106", 38, "Male",   168, 0, 0, 0, 0, 0, 8.5,  "Healthy",   0, 0, 2, 2.8, 22.0, 110, 6, 8, VitalsBehavior.Stable),
        new("P007", "Flutura Mehmeti", "107", 60, "Female", 220, 1, 0, 1, 1, 1, 2.6,  "Average",   0, 1, 7, 7.4, 29.9, 240, 2, 6, VitalsBehavior.Normal),
        new("P008", "Nora Bytyqi", "108", 29, "Female", 160, 0, 0, 0, 0, 0, 9.0,  "Healthy",   0, 0, 1, 2.0, 21.5, 95,  6, 8, VitalsBehavior.Normal),
        new("P009", "Mentor Rexhepi", "109", 75, "Male",   272, 1, 1, 1, 0, 1, 1.1,  "Unhealthy", 1, 1, 9, 9.8, 34.2, 360, 1, 5, VitalsBehavior.Normal),
        new("P010", "Vjosa Kelmendi", "110", 50, "Female", 205, 0, 1, 0, 1, 0, 4.4,  "Average",   1, 0, 6, 5.5, 28.4, 210, 3, 7, VitalsBehavior.Normal)
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
                    "Room {Room}: HR {HeartRate} BPM, SpO2 {Spo2}%, Temp {Temperature}C, BP {Sys}/{Dia}, RR {Resp}",
                    reading.RoomNumber,
                    reading.HeartRate,
                    reading.Spo2,
                    reading.Temperature,
                    reading.SystolicBp,
                    reading.DiastolicBp,
                    reading.RespiratoryRate);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    // Generates realistic vitals with per-patient behavior:
    // - CriticalProne: frequent, sustained critical episodes (Bujar, dhoma 105)
    // - Stable: long healthy stretches with rare mild deviations (Dardan, dhoma 106)
    // - Normal: mixed baseline for the other patients
    private VitalReading CreateReading(PatientSensor p)
    {
        var episode = GetEpisodeState(p);
        var mode = ResolveReadingMode(p, episode);

        int heartRate;
        int spo2;
        double temperature;
        int systolic;
        int diastolic;
        int respiratoryRate;

        if (mode == ReadingMode.Stable)
        {
            heartRate = _random.Next(62, 88);
            spo2 = _random.Next(96, 100);
            temperature = Math.Round(36.5 + (_random.NextDouble() * 0.7), 1);
            systolic = _random.Next(108, 122);
            diastolic = _random.Next(68, 78);
            respiratoryRate = _random.Next(13, 17);
        }
        else if (mode == ReadingMode.Critical)
        {
            // Stack several abnormal vitals so alerts fire consistently during a crisis.
            heartRate = _random.NextDouble() < 0.65 ? _random.Next(125, 168) : _random.Next(38, 49);
            spo2 = _random.Next(84, 89);
            temperature = Math.Round(38.7 + (_random.NextDouble() * 1.3), 1);
            systolic = _random.Next(145, 185);
            diastolic = _random.Next(92, 112);
            respiratoryRate = _random.NextDouble() < 0.7 ? _random.Next(26, 34) : _random.Next(6, 10);
        }
        else if (mode == ReadingMode.Warning)
        {
            // Mild deviation for the stable patient — warning, not critical.
            heartRate = _random.Next(111, 118);
            spo2 = _random.Next(92, 94);
            temperature = Math.Round(37.9 + (_random.NextDouble() * 0.5), 1);
            systolic = _random.Next(132, 139);
            diastolic = _random.Next(86, 89);
            respiratoryRate = _random.Next(21, 24);
        }
        else
        {
            var heartSpike = _random.NextDouble() < 0.08;
            var oxygenDrop = _random.NextDouble() < 0.06;
            var feverSpike = _random.NextDouble() < 0.07;
            var hypertension = _random.NextDouble() < 0.07;
            var tachypnea = _random.NextDouble() < 0.05;

            heartRate = heartSpike
                ? (_random.NextDouble() < 0.5 ? _random.Next(121, 165) : _random.Next(38, 50))
                : _random.Next(60, 101);

            spo2 = oxygenDrop ? _random.Next(85, 92) : _random.Next(95, 101);

            temperature = feverSpike
                ? Math.Round(38.6 + (_random.NextDouble() * 1.4), 1)
                : Math.Round(36.4 + (_random.NextDouble() * 1.4), 1);

            systolic = hypertension ? _random.Next(141, 181) : _random.Next(105, 131);
            diastolic = hypertension ? _random.Next(91, 111) : _random.Next(65, 86);

            respiratoryRate = tachypnea
                ? (_random.NextDouble() < 0.5 ? _random.Next(25, 33) : _random.Next(6, 11))
                : _random.Next(12, 19);
        }

        AdvanceEpisodeState(p, episode, mode);

        return new VitalReading(
            PatientId: p.PatientId,
            PatientName: p.PatientName,
            RoomNumber: p.RoomNumber,
            Age: p.Age,
            HeartRate: heartRate,
            Spo2: spo2,
            Temperature: temperature,
            SystolicBp: systolic,
            DiastolicBp: diastolic,
            RespiratoryRate: respiratoryRate,
            RecordedAt: DateTimeOffset.UtcNow,
            // Static clinical profile (heart-attack dataset features).
            Sex: p.Sex,
            Cholesterol: p.Cholesterol,
            Diabetes: p.Diabetes,
            FamilyHistory: p.FamilyHistory,
            Smoking: p.Smoking,
            Obesity: p.Obesity,
            AlcoholConsumption: p.AlcoholConsumption,
            ExerciseHoursPerWeek: p.ExerciseHoursPerWeek,
            Diet: p.Diet,
            PreviousHeartProblems: p.PreviousHeartProblems,
            MedicationUse: p.MedicationUse,
            StressLevel: p.StressLevel,
            SedentaryHoursPerDay: p.SedentaryHoursPerDay,
            Bmi: p.Bmi,
            Triglycerides: p.Triglycerides,
            PhysicalActivityDaysPerWeek: p.PhysicalActivityDaysPerWeek,
            SleepHoursPerDay: p.SleepHoursPerDay);
    }

    private VitalsEpisodeState GetEpisodeState(PatientSensor p)
    {
        if (!_episodeStates.TryGetValue(p.PatientId, out var state))
        {
            state = new VitalsEpisodeState();
            _episodeStates[p.PatientId] = state;
        }

        return state;
    }

    private ReadingMode ResolveReadingMode(PatientSensor p, VitalsEpisodeState episode)
    {
        if (episode.CriticalReadingsLeft > 0)
            return ReadingMode.Critical;

        if (episode.StableReadingsLeft > 0)
            return ReadingMode.Stable;

        return p.VitalsBehavior switch
        {
            VitalsBehavior.CriticalProne when _random.NextDouble() < 0.32
                => ReadingMode.Critical,
            VitalsBehavior.Stable when _random.NextDouble() < 0.03
                => ReadingMode.Warning,
            VitalsBehavior.Stable
                => ReadingMode.Stable,
            _ => ReadingMode.Normal
        };
    }

    private void AdvanceEpisodeState(PatientSensor p, VitalsEpisodeState episode, ReadingMode mode)
    {
        if (mode == ReadingMode.Critical)
        {
            if (episode.CriticalReadingsLeft <= 0)
                episode.CriticalReadingsLeft = _random.Next(10, 19);

            episode.CriticalReadingsLeft--;
            episode.StableReadingsLeft = 0;
            return;
        }

        episode.CriticalReadingsLeft = 0;

        if (p.VitalsBehavior == VitalsBehavior.Stable && mode != ReadingMode.Warning)
        {
            if (episode.StableReadingsLeft <= 0)
                episode.StableReadingsLeft = _random.Next(25, 41);

            episode.StableReadingsLeft--;
            return;
        }

        episode.StableReadingsLeft = 0;
    }
}

public enum VitalsBehavior
{
    Normal,
    Stable,
    CriticalProne
}

public enum ReadingMode
{
    Stable,
    Warning,
    Normal,
    Critical
}

public sealed class VitalsEpisodeState
{
    public int CriticalReadingsLeft { get; set; }
    public int StableReadingsLeft { get; set; }
}

public sealed record KafkaOptions
{
    public string BootstrapServers { get; init; } = "178.105.181.143:9092";
    public string Topic { get; init; } = "health-vitals";
}

public sealed record PatientSensor(
    string PatientId, string PatientName, string RoomNumber, int Age, string Sex,
    int Cholesterol, int Diabetes, int FamilyHistory, int Smoking, int Obesity, int AlcoholConsumption,
    double ExerciseHoursPerWeek, string Diet, int PreviousHeartProblems, int MedicationUse, int StressLevel,
    double SedentaryHoursPerDay, double Bmi, int Triglycerides, int PhysicalActivityDaysPerWeek, int SleepHoursPerDay,
    VitalsBehavior VitalsBehavior = VitalsBehavior.Normal);

public sealed record VitalReading(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int Age,
    int HeartRate,
    int Spo2,
    double Temperature,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    DateTimeOffset RecordedAt,
    // Clinical profile (heart-attack dataset features) - static per patient.
    string Sex = "Unknown",
    int Cholesterol = 0,
    int Diabetes = 0,
    int FamilyHistory = 0,
    int Smoking = 0,
    int Obesity = 0,
    int AlcoholConsumption = 0,
    double ExerciseHoursPerWeek = 0,
    string Diet = "Average",
    int PreviousHeartProblems = 0,
    int MedicationUse = 0,
    int StressLevel = 0,
    double SedentaryHoursPerDay = 0,
    double Bmi = 0,
    int Triglycerides = 0,
    int PhysicalActivityDaysPerWeek = 0,
    int SleepHoursPerDay = 0);

using System.Text.Json;
using Microsoft.ML;

namespace Vue.Api.Ai;

// Loads the trained heart-attack model and predicts from profile + latest vitals.
public sealed class HeartAttackPredictionService
{
    private readonly ILogger<HeartAttackPredictionService> _logger;
    private readonly MLContext _ml = new(seed: 42);
    private readonly object _lock = new();
    private PredictionEngine<HeartModelInput, HeartModelOutput>? _engine;

    public bool ModelLoaded { get; private set; }
    public string ChosenModel { get; private set; } = "Unavailable";
    public JsonDocument? Metadata { get; private set; }

    public HeartAttackPredictionService(IWebHostEnvironment env, ILogger<HeartAttackPredictionService> logger)
    {
        _logger = logger;
        var dir = Path.Combine(env.ContentRootPath, "AiModels");
        var modelPath = Path.Combine(dir, "heart_attack_model.zip");
        var metaPath = Path.Combine(dir, "model-metrics.json");

        try
        {
            if (File.Exists(modelPath))
            {
                var model = _ml.Model.Load(modelPath, out _);
                _engine = _ml.Model.CreatePredictionEngine<HeartModelInput, HeartModelOutput>(model);
                ModelLoaded = true;
            }
            else
            {
                _logger.LogWarning("Heart-attack model not found at {Path}.", modelPath);
            }

            if (File.Exists(metaPath))
            {
                Metadata = JsonDocument.Parse(File.ReadAllText(metaPath));
                ChosenModel = Metadata.RootElement.TryGetProperty("chosenModel", out var m) ? m.GetString() ?? "Unknown" : "Unknown";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load heart-attack model.");
        }
    }

    public MlPredictionDto Predict(PatientProfileDto p, LivePatientRow? vitals) =>
        PredictCore(p, vitals?.HeartRate ?? 0, vitals?.SystolicBp ?? 0, vitals?.DiastolicBp ?? 0);

    public MlPredictionDto Predict(PatientProfileDto p, VitalReadingDto? vitals) =>
        PredictCore(p, vitals?.HeartRate ?? 0, vitals?.SystolicBp ?? 0, vitals?.DiastolicBp ?? 0);

    private MlPredictionDto PredictCore(PatientProfileDto p, int hr, int systolic, int diastolic)
    {
        var input = new HeartModelInput
        {
            Age = p.Age,
            Sex = string.IsNullOrWhiteSpace(p.Sex) ? "Unknown" : p.Sex,
            Cholesterol = p.Cholesterol,
            Systolic = systolic,
            Diastolic = diastolic,
            HeartRate = hr,
            Diabetes = p.Diabetes,
            FamilyHistory = p.FamilyHistory,
            Smoking = p.Smoking,
            Obesity = p.Obesity,
            AlcoholConsumption = p.AlcoholConsumption,
            ExerciseHoursPerWeek = (float)p.ExerciseHoursPerWeek,
            Diet = string.IsNullOrWhiteSpace(p.Diet) ? "Average" : p.Diet,
            PreviousHeartProblems = p.PreviousHeartProblems,
            MedicationUse = p.MedicationUse,
            StressLevel = p.StressLevel,
            SedentaryHoursPerDay = (float)p.SedentaryHoursPerDay,
            Bmi = (float)p.Bmi,
            Triglycerides = p.Triglycerides,
            PhysicalActivityDaysPerWeek = p.PhysicalActivityDaysPerWeek,
            SleepHoursPerDay = p.SleepHoursPerDay
        };

        double probability;
        if (ModelLoaded && _engine is not null)
        {
            lock (_lock)
            {
                probability = Math.Clamp(_engine.Predict(input).Probability, 0, 1);
            }
        }
        else
        {
            probability = Heuristic(p, hr, systolic);
        }

        var category = probability >= 0.70 ? "HIGH" : probability >= 0.30 ? "MEDIUM" : "LOW";

        return new MlPredictionDto(p.PatientId, p.PatientName, p.RoomNumber, Math.Round(probability, 4),
            category, "Rrezik infarkti", BuildTopFactors(p, hr, systolic, diastolic), hr, systolic, diastolic, DateTimeOffset.UtcNow);
    }

    private static List<string> BuildTopFactors(PatientProfileDto p, int hr, int systolic, int diastolic)
    {
        var f = new List<string>();
        if (p.Cholesterol >= 240) f.Add($"Kolesterol i lartë ({p.Cholesterol} mg/dL)");
        if (p.Bmi >= 30) f.Add($"BMI obez ({p.Bmi:0.0})");
        if (p.Smoking == 1) f.Add("Duhanpirës");
        if (p.Diabetes == 1) f.Add("Diabetik");
        if (systolic > 140 || diastolic > 90) f.Add($"Tension i lartë i gjakut ({systolic}/{diastolic})");
        if (p.FamilyHistory == 1) f.Add("Histori familjare sëmundjeje zemre");
        if (p.PreviousHeartProblems == 1) f.Add("Probleme të mëparshme me zemrën");
        if (p.Triglycerides >= 200) f.Add($"Trigliceridë të larta ({p.Triglycerides})");
        if (p.Age >= 65) f.Add($"Moshë e avancuar ({p.Age})");
        if (hr > 120 || (hr > 0 && hr < 50)) f.Add($"Pulsi i parregullt ({hr} BPM)");
        if (p.StressLevel >= 8) f.Add($"Nivel i lartë stresi ({p.StressLevel}/10)");
        if (f.Count == 0) f.Add("Nuk u zbuluan faktorë të rëndësishëm rreziku");
        return f.Take(6).ToList();
    }

    private static double Heuristic(PatientProfileDto p, int hr, int systolic)
    {
        double s = 0;
        if (p.Cholesterol >= 240) s += .18; if (p.Bmi >= 30) s += .12; if (p.Smoking == 1) s += .12;
        if (p.Diabetes == 1) s += .12; if (systolic > 140) s += .12; if (p.FamilyHistory == 1) s += .1;
        if (p.PreviousHeartProblems == 1) s += .12; if (p.Age >= 65) s += .08; if (p.Triglycerides >= 200) s += .06;
        return Math.Clamp(s, 0, 1);
    }
}

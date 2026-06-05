using System.Text.Json;
using Microsoft.ML;
using Web.Dashboard.Models;

namespace Web.Dashboard.Ai;

// Loads the trained heart-attack model and serves predictions.
// Combines the static patient profile with the latest live vitals (heart rate + blood pressure)
// so the prediction reflects real-time telemetry during the demo.
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
                _logger.LogWarning("Heart-attack model not found at {Path}. Run the AI.Training project first.", modelPath);
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

    // Predicts using the patient profile and the latest live vitals snapshot.
    public MlPredictionDto Predict(PatientProfileDto profile, LivePatientRow? vitals)
    {
        var heartRate = vitals?.HeartRate ?? 0;
        var systolic = vitals?.SystolicBp ?? 0;
        var diastolic = vitals?.DiastolicBp ?? 0;

        var input = new HeartModelInput
        {
            Age = profile.Age,
            Sex = string.IsNullOrWhiteSpace(profile.Sex) ? "Unknown" : profile.Sex,
            Cholesterol = profile.Cholesterol,
            Systolic = systolic,
            Diastolic = diastolic,
            HeartRate = heartRate,
            Diabetes = profile.Diabetes,
            FamilyHistory = profile.FamilyHistory,
            Smoking = profile.Smoking,
            Obesity = profile.Obesity,
            AlcoholConsumption = profile.AlcoholConsumption,
            ExerciseHoursPerWeek = (float)profile.ExerciseHoursPerWeek,
            Diet = string.IsNullOrWhiteSpace(profile.Diet) ? "Average" : profile.Diet,
            PreviousHeartProblems = profile.PreviousHeartProblems,
            MedicationUse = profile.MedicationUse,
            StressLevel = profile.StressLevel,
            SedentaryHoursPerDay = (float)profile.SedentaryHoursPerDay,
            Bmi = (float)profile.Bmi,
            Triglycerides = profile.Triglycerides,
            PhysicalActivityDaysPerWeek = profile.PhysicalActivityDaysPerWeek,
            SleepHoursPerDay = profile.SleepHoursPerDay
        };

        double probability;
        if (ModelLoaded && _engine is not null)
        {
            lock (_lock)
            {
                var output = _engine.Predict(input);
                probability = Math.Clamp(output.Probability, 0, 1);
            }
        }
        else
        {
            // Fallback if the model file is missing: a transparent profile-based heuristic.
            probability = HeuristicProbability(profile, heartRate, systolic);
        }

        var category = probability >= 0.70 ? "HIGH" : probability >= 0.30 ? "MEDIUM" : "LOW";

        return new MlPredictionDto(
            profile.PatientId,
            profile.PatientName,
            profile.RoomNumber,
            Math.Round(probability, 4),
            category,
            "Heart Attack Risk",
            BuildTopFactors(profile, heartRate, systolic, diastolic),
            heartRate,
            systolic,
            diastolic,
            DateTimeOffset.UtcNow);
    }

    // Per-patient explanation: clinical risk factors that are currently abnormal/elevated.
    private static List<string> BuildTopFactors(PatientProfileDto p, int hr, int systolic, int diastolic)
    {
        var factors = new List<string>();
        if (p.Cholesterol >= 240) factors.Add($"High cholesterol ({p.Cholesterol} mg/dL)");
        if (p.Bmi >= 30) factors.Add($"Obese BMI ({p.Bmi:0.0})");
        if (p.Smoking == 1) factors.Add("Smoker");
        if (p.Diabetes == 1) factors.Add("Diabetic");
        if (systolic > 140 || diastolic > 90) factors.Add($"High blood pressure ({systolic}/{diastolic})");
        if (p.FamilyHistory == 1) factors.Add("Family history of heart disease");
        if (p.PreviousHeartProblems == 1) factors.Add("Previous heart problems");
        if (p.Triglycerides >= 200) factors.Add($"High triglycerides ({p.Triglycerides})");
        if (p.Age >= 65) factors.Add($"Advanced age ({p.Age})");
        if (hr > 120 || (hr > 0 && hr < 50)) factors.Add($"Abnormal heart rate ({hr} BPM)");
        if (p.StressLevel >= 8) factors.Add($"High stress level ({p.StressLevel}/10)");
        if (factors.Count == 0) factors.Add("No major risk factors detected");
        return factors.Take(6).ToList();
    }

    private static double HeuristicProbability(PatientProfileDto p, int hr, int systolic)
    {
        double s = 0;
        if (p.Cholesterol >= 240) s += .18; if (p.Bmi >= 30) s += .12; if (p.Smoking == 1) s += .12;
        if (p.Diabetes == 1) s += .12; if (systolic > 140) s += .12; if (p.FamilyHistory == 1) s += .1;
        if (p.PreviousHeartProblems == 1) s += .12; if (p.Age >= 65) s += .08; if (p.Triglycerides >= 200) s += .06;
        return Math.Clamp(s, 0, 1);
    }
}

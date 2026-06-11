using System.Globalization;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;

// =============================================================================
// Heart Attack Risk - ML.NET training pipeline.
// Trains Logistic Regression, Gradient Boosting (FastTree) and Random Forest
// (FastForest), evaluates each (Accuracy / Precision / Recall / F1 / AUC),
// selects the best by F1, persists the model and writes model metadata
// (metrics + permutation feature importance) for the dashboard.
// =============================================================================

var datasetPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Web.Dashboard", "heart_attack_prediction_dataset.csv");

var outputDir = args.Length > 1
    ? args[1]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Vue.Api", "AiModels");

datasetPath = Path.GetFullPath(datasetPath);
outputDir = Path.GetFullPath(outputDir);
Directory.CreateDirectory(outputDir);

Console.WriteLine($"Dataset : {datasetPath}");
Console.WriteLine($"Output  : {outputDir}");

if (!File.Exists(datasetPath))
{
    Console.Error.WriteLine("Dataset not found. Pass the CSV path as the first argument.");
    return 1;
}

var records = LoadRecords(datasetPath);
Console.WriteLine($"Loaded {records.Count} records. Positives: {records.Count(r => r.Label)}");

var ml = new MLContext(seed: 42);
var data = ml.Data.LoadFromEnumerable(records);
var split = ml.Data.TrainTestSplit(data, testFraction: 0.2, seed: 42);

var numericColumns = new[]
{
    "Age", "Cholesterol", "Systolic", "Diastolic", "HeartRate", "Diabetes", "FamilyHistory",
    "Smoking", "Obesity", "AlcoholConsumption", "ExerciseHoursPerWeek", "PreviousHeartProblems",
    "MedicationUse", "StressLevel", "SedentaryHoursPerDay", "Bmi", "Triglycerides",
    "PhysicalActivityDaysPerWeek", "SleepHoursPerDay"
};

// Featurisation: one-hot encode categoricals, concatenate, scale to [0,1].
var featurePipeline = ml.Transforms.Categorical.OneHotEncoding("SexEncoded", "Sex")
    .Append(ml.Transforms.Categorical.OneHotEncoding("DietEncoded", "Diet"))
    .Append(ml.Transforms.Concatenate("Features", numericColumns.Concat(new[] { "SexEncoded", "DietEncoded" }).ToArray()))
    .Append(ml.Transforms.NormalizeMinMax("Features"));

var featureTransformer = featurePipeline.Fit(split.TrainSet);
var trainFeatured = featureTransformer.Transform(split.TrainSet);
var testFeatured = featureTransformer.Transform(split.TestSet);

var results = new List<(string Name, ITransformer Model, CalibratedBinaryClassificationMetrics Metrics)>();

// 1. Logistic Regression
{
    var trainer = ml.BinaryClassification.Trainers.LbfgsLogisticRegression("Label", "Features");
    var model = trainer.Fit(trainFeatured);
    var metrics = ml.BinaryClassification.Evaluate(model.Transform(testFeatured), "Label");
    results.Add(("Logistic Regression", model, metrics));
    Print("Logistic Regression", metrics);
}

// 2. Gradient Boosting (FastTree)
{
    var trainer = ml.BinaryClassification.Trainers.FastTree("Label", "Features", numberOfTrees: 200, numberOfLeaves: 32);
    var model = trainer.Fit(trainFeatured);
    var metrics = ml.BinaryClassification.Evaluate(model.Transform(testFeatured), "Label");
    results.Add(("Gradient Boosting", model, metrics));
    Print("Gradient Boosting", metrics);
}

// 3. Random Forest (FastForest + Platt calibrator so it emits a probability)
{
    var trainer = ml.BinaryClassification.Trainers.FastForest("Label", "Features", numberOfTrees: 200, numberOfLeaves: 32)
        .Append(ml.BinaryClassification.Calibrators.Platt("Label", "Score"));
    var model = trainer.Fit(trainFeatured);
    var metrics = ml.BinaryClassification.Evaluate(model.Transform(testFeatured), "Label");
    results.Add(("Random Forest", model, metrics));
    Print("Random Forest", metrics);
}

// Select best by F1 (robust under class imbalance).
var best = results.OrderByDescending(r => r.Metrics.F1Score).First();
Console.WriteLine($"\n=> Best model: {best.Name} (F1 = {best.Metrics.F1Score:0.0000})");

// Persist the full pipeline (featurisation + selected model).
var fullModel = featureTransformer.Append(best.Model);
var modelPath = Path.Combine(outputDir, "heart_attack_model.zip");
ml.Model.Save(fullModel, split.TrainSet.Schema, modelPath);
Console.WriteLine($"Saved model to {modelPath}");

// Model-agnostic permutation feature importance over original features.
var importance = ComputePermutationImportance(ml, fullModel, records.Where((_, i) => i % 5 == 0).ToList());

// Confusion matrix for the selected model (actual rows x predicted columns).
var confusion = best.Metrics.ConfusionMatrix;
var confusionCounts = confusion.Counts.Select(row => row.Select(v => (int)v).ToArray()).ToArray();

// Classification threshold applied on the calibrated probability (ML.NET default = 0.5).
const double classificationThreshold = 0.5;

var metadata = new
{
    chosenModel = best.Name,
    trainedAt = DateTimeOffset.UtcNow,
    rows = records.Count,
    positives = records.Count(r => r.Label),
    selectionMetric = "F1Score",
    classificationThreshold,
    metrics = results.Select(r => new
    {
        model = r.Name,
        accuracy = Math.Round(r.Metrics.Accuracy, 4),
        precision = Math.Round(r.Metrics.PositivePrecision, 4),
        recall = Math.Round(r.Metrics.PositiveRecall, 4),
        f1 = Math.Round(r.Metrics.F1Score, 4),
        auc = Math.Round(r.Metrics.AreaUnderRocCurve, 4)
    }),
    confusionMatrix = new
    {
        model = best.Name,
        counts = confusionCounts,
        perClassPrecision = confusion.PerClassPrecision.Select(v => Math.Round(v, 4)).ToArray(),
        perClassRecall = confusion.PerClassRecall.Select(v => Math.Round(v, 4)).ToArray()
    },
    topFactors = importance
        .OrderByDescending(kv => kv.Value)
        .Take(10)
        .Select(kv => new { factor = Pretty(kv.Key), importance = Math.Round(kv.Value, 5) })
};

var metaPath = Path.Combine(outputDir, "model-metrics.json");
await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Saved metadata to {metaPath}");
return 0;

// ---------------- helpers ----------------

void Print(string name, CalibratedBinaryClassificationMetrics m) =>
    Console.WriteLine($"{name,-22} Acc={m.Accuracy:0.000} Prec={m.PositivePrecision:0.000} Rec={m.PositiveRecall:0.000} F1={m.F1Score:0.000} AUC={m.AreaUnderRocCurve:0.000}");

static List<HeartRecord> LoadRecords(string path)
{
    var list = new List<HeartRecord>();
    var ci = CultureInfo.InvariantCulture;

    foreach (var line in File.ReadLines(path).Skip(1))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        var c = line.Split(',');
        if (c.Length < 26) continue;

        var bp = c[4].Split('/');
        list.Add(new HeartRecord
        {
            Age = float.Parse(c[1], ci),
            Sex = c[2],
            Cholesterol = float.Parse(c[3], ci),
            Systolic = bp.Length > 0 && float.TryParse(bp[0], NumberStyles.Any, ci, out var s) ? s : 0,
            Diastolic = bp.Length > 1 && float.TryParse(bp[1], NumberStyles.Any, ci, out var d) ? d : 0,
            HeartRate = float.Parse(c[5], ci),
            Diabetes = float.Parse(c[6], ci),
            FamilyHistory = float.Parse(c[7], ci),
            Smoking = float.Parse(c[8], ci),
            Obesity = float.Parse(c[9], ci),
            AlcoholConsumption = float.Parse(c[10], ci),
            ExerciseHoursPerWeek = float.Parse(c[11], ci),
            Diet = c[12],
            PreviousHeartProblems = float.Parse(c[13], ci),
            MedicationUse = float.Parse(c[14], ci),
            StressLevel = float.Parse(c[15], ci),
            SedentaryHoursPerDay = float.Parse(c[16], ci),
            Bmi = float.Parse(c[18], ci),
            Triglycerides = float.Parse(c[19], ci),
            PhysicalActivityDaysPerWeek = float.Parse(c[20], ci),
            SleepHoursPerDay = float.Parse(c[21], ci),
            Label = c[25].Trim() == "1"
        });
    }

    return list;
}

// Permutation importance: shuffle one feature at a time and measure the drop in AUC.
static Dictionary<string, double> ComputePermutationImportance(MLContext ml, ITransformer model, List<HeartRecord> sample)
{
    double Auc(IEnumerable<HeartRecord> recs)
    {
        var dv = ml.Data.LoadFromEnumerable(recs);
        var metrics = ml.BinaryClassification.Evaluate(model.Transform(dv), "Label");
        return metrics.AreaUnderRocCurve;
    }

    var baseline = Auc(sample);
    var rng = new Random(7);
    var features = new[]
    {
        "Age", "Sex", "Cholesterol", "Systolic", "Diastolic", "HeartRate", "Diabetes", "FamilyHistory",
        "Smoking", "Obesity", "AlcoholConsumption", "ExerciseHoursPerWeek", "Diet", "PreviousHeartProblems",
        "MedicationUse", "StressLevel", "SedentaryHoursPerDay", "Bmi", "Triglycerides",
        "PhysicalActivityDaysPerWeek", "SleepHoursPerDay"
    };

    var result = new Dictionary<string, double>();

    foreach (var feature in features)
    {
        var permuted = sample.Select(Clone).ToList();
        var values = permuted.Select(r => GetValue(r, feature)).OrderBy(_ => rng.Next()).ToList();
        for (var i = 0; i < permuted.Count; i++) SetValue(permuted[i], feature, values[i]);
        result[feature] = Math.Max(0, baseline - Auc(permuted));
    }

    return result;
}

static HeartRecord Clone(HeartRecord r) => new()
{
    Age = r.Age, Sex = r.Sex, Cholesterol = r.Cholesterol, Systolic = r.Systolic, Diastolic = r.Diastolic,
    HeartRate = r.HeartRate, Diabetes = r.Diabetes, FamilyHistory = r.FamilyHistory, Smoking = r.Smoking,
    Obesity = r.Obesity, AlcoholConsumption = r.AlcoholConsumption, ExerciseHoursPerWeek = r.ExerciseHoursPerWeek,
    Diet = r.Diet, PreviousHeartProblems = r.PreviousHeartProblems, MedicationUse = r.MedicationUse,
    StressLevel = r.StressLevel, SedentaryHoursPerDay = r.SedentaryHoursPerDay, Bmi = r.Bmi,
    Triglycerides = r.Triglycerides, PhysicalActivityDaysPerWeek = r.PhysicalActivityDaysPerWeek,
    SleepHoursPerDay = r.SleepHoursPerDay, Label = r.Label
};

static object GetValue(HeartRecord r, string f) => f switch
{
    "Sex" => r.Sex, "Diet" => r.Diet,
    "Age" => r.Age, "Cholesterol" => r.Cholesterol, "Systolic" => r.Systolic, "Diastolic" => r.Diastolic,
    "HeartRate" => r.HeartRate, "Diabetes" => r.Diabetes, "FamilyHistory" => r.FamilyHistory, "Smoking" => r.Smoking,
    "Obesity" => r.Obesity, "AlcoholConsumption" => r.AlcoholConsumption, "ExerciseHoursPerWeek" => r.ExerciseHoursPerWeek,
    "PreviousHeartProblems" => r.PreviousHeartProblems, "MedicationUse" => r.MedicationUse, "StressLevel" => r.StressLevel,
    "SedentaryHoursPerDay" => r.SedentaryHoursPerDay, "Bmi" => r.Bmi, "Triglycerides" => r.Triglycerides,
    "PhysicalActivityDaysPerWeek" => r.PhysicalActivityDaysPerWeek, "SleepHoursPerDay" => r.SleepHoursPerDay,
    _ => 0f
};

static void SetValue(HeartRecord r, string f, object v)
{
    switch (f)
    {
        case "Sex": r.Sex = (string)v; break;
        case "Diet": r.Diet = (string)v; break;
        case "Age": r.Age = (float)v; break;
        case "Cholesterol": r.Cholesterol = (float)v; break;
        case "Systolic": r.Systolic = (float)v; break;
        case "Diastolic": r.Diastolic = (float)v; break;
        case "HeartRate": r.HeartRate = (float)v; break;
        case "Diabetes": r.Diabetes = (float)v; break;
        case "FamilyHistory": r.FamilyHistory = (float)v; break;
        case "Smoking": r.Smoking = (float)v; break;
        case "Obesity": r.Obesity = (float)v; break;
        case "AlcoholConsumption": r.AlcoholConsumption = (float)v; break;
        case "ExerciseHoursPerWeek": r.ExerciseHoursPerWeek = (float)v; break;
        case "PreviousHeartProblems": r.PreviousHeartProblems = (float)v; break;
        case "MedicationUse": r.MedicationUse = (float)v; break;
        case "StressLevel": r.StressLevel = (float)v; break;
        case "SedentaryHoursPerDay": r.SedentaryHoursPerDay = (float)v; break;
        case "Bmi": r.Bmi = (float)v; break;
        case "Triglycerides": r.Triglycerides = (float)v; break;
        case "PhysicalActivityDaysPerWeek": r.PhysicalActivityDaysPerWeek = (float)v; break;
        case "SleepHoursPerDay": r.SleepHoursPerDay = (float)v; break;
    }
}

static string Pretty(string f) => f switch
{
    "Systolic" => "Blood Pressure (Systolic)",
    "Diastolic" => "Blood Pressure (Diastolic)",
    "Bmi" => "BMI",
    "HeartRate" => "Heart Rate",
    "FamilyHistory" => "Family History",
    "AlcoholConsumption" => "Alcohol Consumption",
    "ExerciseHoursPerWeek" => "Exercise Hours/Week",
    "PreviousHeartProblems" => "Previous Heart Problems",
    "MedicationUse" => "Medication Use",
    "StressLevel" => "Stress Level",
    "SedentaryHoursPerDay" => "Sedentary Hours/Day",
    "PhysicalActivityDaysPerWeek" => "Physical Activity Days/Week",
    "SleepHoursPerDay" => "Sleep Hours/Day",
    _ => f
};

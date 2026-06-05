using Microsoft.ML.Data;

namespace Vue.Api.Ai;

public sealed class HeartModelInput
{
    public float Age { get; set; }
    public string Sex { get; set; } = "Unknown";
    public float Cholesterol { get; set; }
    public float Systolic { get; set; }
    public float Diastolic { get; set; }
    public float HeartRate { get; set; }
    public float Diabetes { get; set; }
    public float FamilyHistory { get; set; }
    public float Smoking { get; set; }
    public float Obesity { get; set; }
    public float AlcoholConsumption { get; set; }
    public float ExerciseHoursPerWeek { get; set; }
    public string Diet { get; set; } = "Average";
    public float PreviousHeartProblems { get; set; }
    public float MedicationUse { get; set; }
    public float StressLevel { get; set; }
    public float SedentaryHoursPerDay { get; set; }
    public float Bmi { get; set; }
    public float Triglycerides { get; set; }
    public float PhysicalActivityDaysPerWeek { get; set; }
    public float SleepHoursPerDay { get; set; }
}

public sealed class HeartModelOutput
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}

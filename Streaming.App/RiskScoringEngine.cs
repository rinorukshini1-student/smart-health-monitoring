// AI component: a rule-based intelligent scoring engine (weighted clinical risk model).
//
// This is intentionally implemented as a transparent, explainable scoring engine rather than
// a black-box ML model because the simulator does not produce labelled historical data for
// supervised training. The design mirrors early-warning scores (e.g. NEWS) used in hospitals:
// each vital contributes points based on how far it deviates from the healthy range, the points
// are combined into a 0-100 risk score, and the contributing factors are returned for explanation.
//
// The module is cleanly separated so it can later be swapped for a trained ML model
// (Logistic Regression / Random Forest / Gradient Boosting) exposing the same RiskScore output.
public static class RiskScoringEngine
{
    public const string Low = "Low Risk";
    public const string Medium = "Medium Risk";
    public const string High = "High Risk";

    public static RiskScore Assess(VitalReading r)
    {
        var factors = new List<string>();
        double score = 0;

        score += ScoreHeartRate(r.HeartRate, factors);
        score += ScoreSpo2(r.Spo2, factors);
        score += ScoreTemperature(r.Temperature, factors);
        score += ScoreBloodPressure(r.SystolicBp, r.DiastolicBp, factors);
        score += ScoreRespiratory(r.RespiratoryRate, factors);
        score += ScoreAge(r.Age, factors);

        var bounded = (int)Math.Round(Math.Clamp(score, 0, 100));
        var category = bounded >= 60 ? High : bounded >= 30 ? Medium : Low;

        if (factors.Count == 0)
        {
            factors.Add("All vitals within normal range");
        }

        return new RiskScore(
            r.PatientId,
            r.RoomNumber,
            bounded,
            category,
            string.Join("; ", factors),
            r.HeartRate,
            r.Spo2,
            r.Temperature,
            r.SystolicBp,
            r.DiastolicBp,
            r.RespiratoryRate,
            r.RecordedAt);
    }

    private static double ScoreHeartRate(int hr, List<string> factors)
    {
        if (hr >= 60 && hr <= 100) return 0;
        if (hr > 120 || hr < 50) { factors.Add($"Critical heart rate ({hr} BPM)"); return 28; }
        if (hr > 110 || hr < 55) { factors.Add($"Elevated heart rate ({hr} BPM)"); return 16; }
        factors.Add($"Borderline heart rate ({hr} BPM)"); return 8;
    }

    private static double ScoreSpo2(int spo2, List<string> factors)
    {
        if (spo2 >= 95) return 0;
        if (spo2 < 90) { factors.Add($"Critical oxygen saturation ({spo2}%)"); return 28; }
        if (spo2 < 94) { factors.Add($"Low oxygen saturation ({spo2}%)"); return 16; }
        factors.Add($"Borderline oxygen saturation ({spo2}%)"); return 8;
    }

    private static double ScoreTemperature(double temp, List<string> factors)
    {
        if (temp >= 36.0 && temp <= 37.5) return 0;
        if (temp > 38.5 || temp < 35.0) { factors.Add($"Critical temperature ({temp:0.0}C)"); return 22; }
        if (temp > 37.8) { factors.Add($"Fever ({temp:0.0}C)"); return 12; }
        factors.Add($"Temperature deviation ({temp:0.0}C)"); return 6;
    }

    private static double ScoreBloodPressure(int sys, int dia, List<string> factors)
    {
        if (sys > 140 || dia > 90) { factors.Add($"Hypertension ({sys}/{dia} mmHg)"); return 18; }
        if (sys > 130 || dia > 85) { factors.Add($"Elevated blood pressure ({sys}/{dia} mmHg)"); return 9; }
        if (sys < 90 || dia < 60) { factors.Add($"Hypotension ({sys}/{dia} mmHg)"); return 12; }
        return 0;
    }

    private static double ScoreRespiratory(int rr, List<string> factors)
    {
        if (rr >= 12 && rr <= 20) return 0;
        if (rr > 24 || rr < 10) { factors.Add($"Critical respiratory rate ({rr}/min)"); return 18; }
        factors.Add($"Abnormal respiratory rate ({rr}/min)"); return 9;
    }

    private static double ScoreAge(int age, List<string> factors)
    {
        if (age >= 75) { factors.Add($"Advanced age ({age})"); return 8; }
        if (age >= 65) { factors.Add($"Age risk factor ({age})"); return 4; }
        return 0;
    }
}

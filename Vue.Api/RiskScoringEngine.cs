namespace Vue.Api;

// Rule-based clinical early-warning scoring engine (transparent AI risk score 0-100).
public static class RiskScoringEngine
{
    public static RiskScoreDto Assess(VitalReadingDto r, int age)
    {
        var factors = new List<string>();
        double score = 0;

        score += ScoreHeartRate(r.HeartRate, factors);
        score += ScoreSpo2(r.Spo2, factors);
        score += ScoreTemperature(r.Temperature, factors);
        score += ScoreBloodPressure(r.SystolicBp, r.DiastolicBp, factors);
        score += ScoreRespiratory(r.RespiratoryRate, factors);
        score += ScoreAge(age, factors);

        var bounded = (int)Math.Round(Math.Clamp(score, 0, 100));
        var category = bounded >= 60 ? "High Risk" : bounded >= 30 ? "Medium Risk" : "Low Risk";
        if (factors.Count == 0) factors.Add("All vitals within normal range");

        return new RiskScoreDto(r.PatientId, r.RoomNumber, bounded, category, string.Join("; ", factors),
            r.HeartRate, r.Spo2, r.Temperature, r.SystolicBp, r.DiastolicBp, r.RespiratoryRate, r.RecordedAt);
    }

    private static double ScoreHeartRate(int hr, List<string> f)
    {
        if (hr is >= 60 and <= 100) return 0;
        if (hr > 120 || hr < 50) { f.Add($"Critical heart rate ({hr} BPM)"); return 28; }
        if (hr > 110 || hr < 55) { f.Add($"Elevated heart rate ({hr} BPM)"); return 16; }
        f.Add($"Borderline heart rate ({hr} BPM)"); return 8;
    }

    private static double ScoreSpo2(int spo2, List<string> f)
    {
        if (spo2 >= 95) return 0;
        if (spo2 < 90) { f.Add($"Critical oxygen saturation ({spo2}%)"); return 28; }
        if (spo2 < 94) { f.Add($"Low oxygen saturation ({spo2}%)"); return 16; }
        f.Add($"Borderline oxygen saturation ({spo2}%)"); return 8;
    }

    private static double ScoreTemperature(double t, List<string> f)
    {
        if (t is >= 36.0 and <= 37.5) return 0;
        if (t > 38.5 || t < 35.0) { f.Add($"Critical temperature ({t:0.0}C)"); return 22; }
        if (t > 37.8) { f.Add($"Fever ({t:0.0}C)"); return 12; }
        f.Add($"Temperature deviation ({t:0.0}C)"); return 6;
    }

    private static double ScoreBloodPressure(int sys, int dia, List<string> f)
    {
        if (sys > 140 || dia > 90) { f.Add($"Hypertension ({sys}/{dia} mmHg)"); return 18; }
        if (sys > 130 || dia > 85) { f.Add($"Elevated blood pressure ({sys}/{dia} mmHg)"); return 9; }
        if (sys < 90 || dia < 60) { f.Add($"Hypotension ({sys}/{dia} mmHg)"); return 12; }
        return 0;
    }

    private static double ScoreRespiratory(int rr, List<string> f)
    {
        if (rr is >= 12 and <= 20) return 0;
        if (rr > 24 || rr < 10) { f.Add($"Critical respiratory rate ({rr}/min)"); return 18; }
        f.Add($"Abnormal respiratory rate ({rr}/min)"); return 9;
    }

    private static double ScoreAge(int age, List<string> f)
    {
        if (age >= 75) { f.Add($"Advanced age ({age})"); return 8; }
        if (age >= 65) { f.Add($"Age risk factor ({age})"); return 4; }
        return 0;
    }
}

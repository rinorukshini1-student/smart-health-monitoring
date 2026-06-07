namespace Vue.Api;

// Motori i vlerësimit të rrezikut klinik (rezultat transparent AI 0–100).
public static class RiskScoringEngine
{
    public const string HighRisk = "Rrezik i Lartë";
    public const string MediumRisk = "Rrezik Mesatar";
    public const string LowRisk = "Rrezik i Ulët";

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
        var category = bounded >= 60 ? HighRisk : bounded >= 30 ? MediumRisk : LowRisk;
        if (factors.Count == 0) factors.Add("Të gjitha vitalët brenda normës");

        return new RiskScoreDto(r.PatientId, r.RoomNumber, bounded, category, string.Join("; ", factors),
            r.HeartRate, r.Spo2, r.Temperature, r.SystolicBp, r.DiastolicBp, r.RespiratoryRate, r.RecordedAt);
    }

    private static double ScoreHeartRate(int hr, List<string> f)
    {
        if (hr is >= 60 and <= 100) return 0;
        if (hr > 120 || hr < 50) { f.Add($"Pulsi kritik ({hr} BPM)"); return 28; }
        if (hr > 110 || hr < 55) { f.Add($"Pulsi i ngritur ({hr} BPM)"); return 16; }
        f.Add($"Pulsi kufitar ({hr} BPM)"); return 8;
    }

    private static double ScoreSpo2(int spo2, List<string> f)
    {
        if (spo2 >= 95) return 0;
        if (spo2 < 90) { f.Add($"Saturim kritik i oksigjenit ({spo2}%)"); return 28; }
        if (spo2 < 94) { f.Add($"Saturim i ulët i oksigjenit ({spo2}%)"); return 16; }
        f.Add($"Saturim kufitar i oksigjenit ({spo2}%)"); return 8;
    }

    private static double ScoreTemperature(double t, List<string> f)
    {
        if (t is >= 36.0 and <= 37.5) return 0;
        if (t > 38.5 || t < 35.0) { f.Add($"Temperaturë kritike ({t:0.0}°C)"); return 22; }
        if (t > 37.8) { f.Add($"Ethe ({t:0.0}°C)"); return 12; }
        f.Add($"Devijim temperature ({t:0.0}°C)"); return 6;
    }

    private static double ScoreBloodPressure(int sys, int dia, List<string> f)
    {
        if (sys > 140 || dia > 90) { f.Add($"Hipertension ({sys}/{dia} mmHg)"); return 18; }
        if (sys > 130 || dia > 85) { f.Add($"Tension i ngritur i gjakut ({sys}/{dia} mmHg)"); return 9; }
        if (sys < 90 || dia < 60) { f.Add($"Hipotension ({sys}/{dia} mmHg)"); return 12; }
        return 0;
    }

    private static double ScoreRespiratory(int rr, List<string> f)
    {
        if (rr is >= 12 and <= 20) return 0;
        if (rr > 24 || rr < 10) { f.Add($"Frekuencë kritike e frymëmarrjes ({rr}/min)"); return 18; }
        f.Add($"Frekuencë e parregullt e frymëmarrjes ({rr}/min)"); return 9;
    }

    private static double ScoreAge(int age, List<string> f)
    {
        if (age >= 75) { f.Add($"Moshë e avancuar ({age})"); return 8; }
        if (age >= 65) { f.Add($"Faktor rreziku nga mosha ({age})"); return 4; }
        return 0;
    }
}

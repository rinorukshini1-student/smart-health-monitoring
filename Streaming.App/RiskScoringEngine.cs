// Komponent AI: motor transparent i vlerësimit të rrezikut klinik (model i ponderuar 0–100).
//
// Implementuar si motor i shpjegueshëm (jo black-box ML) sepse simulatori nuk prodhon
// të dhëna të etiketuara për trajnim të mbikëqyrur. Dizajni pasqyron skorët e hershëm
// paralajmërues (p.sh. NEWS): çdo vital kontribuon pikë sipas devijimit nga norma,
// kombinohen në një rezultat 0–100, dhe faktorët kthehen për shpjegim.
//
// Moduli është i ndarë qartë që më vonë mund të zëvendësohet me një model ML i trajnuar
// (Logistic Regression / Random Forest / Gradient Boosting) me të njëjtin output RiskScore.
public static class RiskScoringEngine
{
    public const string Low = "Rrezik i Ulët";
    public const string Medium = "Rrezik Mesatar";
    public const string High = "Rrezik i Lartë";

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
            factors.Add("Të gjitha vitalët brenda normës");
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
        if (hr > 120 || hr < 50) { factors.Add($"Pulsi kritik ({hr} BPM)"); return 28; }
        if (hr > 110 || hr < 55) { factors.Add($"Pulsi i ngritur ({hr} BPM)"); return 16; }
        factors.Add($"Pulsi kufitar ({hr} BPM)"); return 8;
    }

    private static double ScoreSpo2(int spo2, List<string> factors)
    {
        if (spo2 >= 95) return 0;
        if (spo2 < 90) { factors.Add($"Saturim kritik i oksigjenit ({spo2}%)"); return 28; }
        if (spo2 < 94) { factors.Add($"Saturim i ulët i oksigjenit ({spo2}%)"); return 16; }
        factors.Add($"Saturim kufitar i oksigjenit ({spo2}%)"); return 8;
    }

    private static double ScoreTemperature(double temp, List<string> factors)
    {
        if (temp >= 36.0 && temp <= 37.5) return 0;
        if (temp > 38.5 || temp < 35.0) { factors.Add($"Temperaturë kritike ({temp:0.0}°C)"); return 22; }
        if (temp > 37.8) { factors.Add($"Ethe ({temp:0.0}°C)"); return 12; }
        factors.Add($"Devijim temperature ({temp:0.0}°C)"); return 6;
    }

    private static double ScoreBloodPressure(int sys, int dia, List<string> factors)
    {
        if (sys > 140 || dia > 90) { factors.Add($"Hipertension ({sys}/{dia} mmHg)"); return 18; }
        if (sys > 130 || dia > 85) { factors.Add($"Tension i ngritur i gjakut ({sys}/{dia} mmHg)"); return 9; }
        if (sys < 90 || dia < 60) { factors.Add($"Hipotension ({sys}/{dia} mmHg)"); return 12; }
        return 0;
    }

    private static double ScoreRespiratory(int rr, List<string> factors)
    {
        if (rr >= 12 && rr <= 20) return 0;
        if (rr > 24 || rr < 10) { factors.Add($"Frekuencë kritike e frymëmarrjes ({rr}/min)"); return 18; }
        factors.Add($"Frekuencë e parregullt e frymëmarrjes ({rr}/min)"); return 9;
    }

    private static double ScoreAge(int age, List<string> factors)
    {
        if (age >= 75) { factors.Add($"Moshë e avancuar ({age})"); return 8; }
        if (age >= 65) { factors.Add($"Faktor rreziku nga mosha ({age})"); return 4; }
        return 0;
    }
}

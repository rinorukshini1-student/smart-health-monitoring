namespace Vue.Api;

// Alert rule engine - one alert per breached clinical threshold (INFO/WARNING/CRITICAL).
public static class AlertRules
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";

    public static IReadOnlyList<AlertMessageDto> Evaluate(VitalReadingDto r)
    {
        var alerts = new List<AlertMessageDto>();

        if (r.HeartRate > 120) alerts.Add(Build(r, "HeartRate", Critical, $"High heart rate {r.HeartRate} BPM", r.HeartRate));
        else if (r.HeartRate < 50) alerts.Add(Build(r, "HeartRate", Critical, $"Low heart rate {r.HeartRate} BPM", r.HeartRate));
        else if (r.HeartRate > 110 || r.HeartRate < 55) alerts.Add(Build(r, "HeartRate", Warning, $"Heart rate trending {r.HeartRate} BPM", r.HeartRate));

        if (r.Temperature > 38.5) alerts.Add(Build(r, "Temperature", Critical, $"High temperature {r.Temperature:0.0}C", r.Temperature));
        else if (r.Temperature > 37.8) alerts.Add(Build(r, "Temperature", Warning, $"Elevated temperature {r.Temperature:0.0}C", r.Temperature));

        if (r.Spo2 < 90) alerts.Add(Build(r, "SpO2", Critical, $"Low oxygen saturation {r.Spo2}%", r.Spo2));
        else if (r.Spo2 < 94) alerts.Add(Build(r, "SpO2", Warning, $"Oxygen saturation {r.Spo2}%", r.Spo2));

        if (r.SystolicBp > 140 || r.DiastolicBp > 90)
            alerts.Add(Build(r, "BloodPressure", Critical, $"High blood pressure {r.SystolicBp}/{r.DiastolicBp} mmHg", r.SystolicBp));
        else if (r.SystolicBp > 130 || r.DiastolicBp > 85)
            alerts.Add(Build(r, "BloodPressure", Warning, $"Blood pressure trending {r.SystolicBp}/{r.DiastolicBp} mmHg", r.SystolicBp));

        if (r.RespiratoryRate > 24 || r.RespiratoryRate < 10)
            alerts.Add(Build(r, "RespiratoryRate", Critical, $"Abnormal respiratory rate {r.RespiratoryRate}/min", r.RespiratoryRate));
        else if (r.RespiratoryRate > 22 || r.RespiratoryRate < 11)
            alerts.Add(Build(r, "RespiratoryRate", Warning, $"Respiratory rate {r.RespiratoryRate}/min", r.RespiratoryRate));

        return alerts;
    }

    public static string Classify(VitalReadingDto v)
    {
        if (v.HeartRate > 120 || v.HeartRate < 50 || v.Spo2 < 90 || v.Temperature > 38.5
            || v.SystolicBp > 140 || v.DiastolicBp > 90 || v.RespiratoryRate > 24 || v.RespiratoryRate < 10)
            return "Critical";
        if (v.HeartRate > 110 || v.HeartRate < 55 || v.Spo2 < 94 || v.Temperature > 37.8
            || v.SystolicBp > 130 || v.DiastolicBp > 85 || v.RespiratoryRate > 22 || v.RespiratoryRate < 11)
            return "Warning";
        return "Normal";
    }

    private static AlertMessageDto Build(VitalReadingDto r, string type, string severity, string message, double value) =>
        new(Guid.NewGuid(), r.PatientId, r.RoomNumber, type, severity, message, value,
            r.HeartRate, r.Spo2, r.Temperature, r.SystolicBp, r.DiastolicBp, r.RespiratoryRate, r.RecordedAt);
}

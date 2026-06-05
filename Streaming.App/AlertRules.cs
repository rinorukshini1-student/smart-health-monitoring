// Centralised alert rule engine.
// Evaluates every required clinical threshold and emits one alert per breached rule.
// Severity levels follow the project spec: INFO, WARNING, CRITICAL.
public static class AlertRules
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";
    public const string Info = "INFO";

    public static IReadOnlyList<AlertMessage> Evaluate(VitalReading reading)
    {
        var alerts = new List<AlertMessage>();

        // Heart rate: critical if < 50 or > 120, warning when approaching those bounds.
        if (reading.HeartRate > 120)
        {
            alerts.Add(Build(reading, "HeartRate", Critical, $"High heart rate {reading.HeartRate} BPM", reading.HeartRate));
        }
        else if (reading.HeartRate < 50)
        {
            alerts.Add(Build(reading, "HeartRate", Critical, $"Low heart rate {reading.HeartRate} BPM", reading.HeartRate));
        }
        else if (reading.HeartRate > 110 || reading.HeartRate < 55)
        {
            alerts.Add(Build(reading, "HeartRate", Warning, $"Heart rate trending {reading.HeartRate} BPM", reading.HeartRate));
        }

        // Temperature: critical fever above 38.5C, warning above 37.8C.
        if (reading.Temperature > 38.5)
        {
            alerts.Add(Build(reading, "Temperature", Critical, $"High temperature {reading.Temperature:0.0}C", reading.Temperature));
        }
        else if (reading.Temperature > 37.8)
        {
            alerts.Add(Build(reading, "Temperature", Warning, $"Elevated temperature {reading.Temperature:0.0}C", reading.Temperature));
        }

        // SpO2: critical below 90%, warning below 94%.
        if (reading.Spo2 < 90)
        {
            alerts.Add(Build(reading, "SpO2", Critical, $"Low oxygen saturation {reading.Spo2}%", reading.Spo2));
        }
        else if (reading.Spo2 < 94)
        {
            alerts.Add(Build(reading, "SpO2", Warning, $"Oxygen saturation {reading.Spo2}%", reading.Spo2));
        }

        // Blood pressure: critical when systolic > 140 or diastolic > 90.
        if (reading.SystolicBp > 140 || reading.DiastolicBp > 90)
        {
            alerts.Add(Build(reading, "BloodPressure", Critical,
                $"High blood pressure {reading.SystolicBp}/{reading.DiastolicBp} mmHg", reading.SystolicBp));
        }
        else if (reading.SystolicBp > 130 || reading.DiastolicBp > 85)
        {
            alerts.Add(Build(reading, "BloodPressure", Warning,
                $"Blood pressure trending {reading.SystolicBp}/{reading.DiastolicBp} mmHg", reading.SystolicBp));
        }

        // Respiratory rate: critical outside 10-24, warning when borderline.
        if (reading.RespiratoryRate > 24 || reading.RespiratoryRate < 10)
        {
            alerts.Add(Build(reading, "RespiratoryRate", Critical,
                $"Abnormal respiratory rate {reading.RespiratoryRate}/min", reading.RespiratoryRate));
        }
        else if (reading.RespiratoryRate > 22 || reading.RespiratoryRate < 11)
        {
            alerts.Add(Build(reading, "RespiratoryRate", Warning,
                $"Respiratory rate {reading.RespiratoryRate}/min", reading.RespiratoryRate));
        }

        return alerts;
    }

    private static AlertMessage Build(VitalReading reading, string type, string severity, string message, double value) =>
        new(
            Guid.NewGuid(),
            reading.PatientId,
            reading.RoomNumber,
            type,
            severity,
            message,
            value,
            reading.HeartRate,
            reading.Spo2,
            reading.Temperature,
            reading.SystolicBp,
            reading.DiastolicBp,
            reading.RespiratoryRate,
            reading.RecordedAt);
}

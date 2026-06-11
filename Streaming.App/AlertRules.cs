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

        // Pulsi: kritik nëse < 50 ose > 120, paralajmërim kur i afërt kufijve.
        if (reading.HeartRate > 120)
        {
            alerts.Add(Build(reading, "HeartRate", Critical, $"Pulsi i lartë {reading.HeartRate} BPM", reading.HeartRate));
        }
        else if (reading.HeartRate < 50)
        {
            alerts.Add(Build(reading, "HeartRate", Critical, $"Pulsi i ulët {reading.HeartRate} BPM", reading.HeartRate));
        }
        else if (reading.HeartRate > 110 || reading.HeartRate < 55)
        {
            alerts.Add(Build(reading, "HeartRate", Warning, $"Pulsi jashtë normës {reading.HeartRate} BPM", reading.HeartRate));
        }

        // Temperatura: kritike mbi 38.5°C, paralajmërim mbi 37.8°C.
        if (reading.Temperature > 38.5)
        {
            alerts.Add(Build(reading, "Temperature", Critical, $"Temperaturë e lartë {reading.Temperature:0.0}°C", reading.Temperature));
        }
        else if (reading.Temperature > 37.8)
        {
            alerts.Add(Build(reading, "Temperature", Warning, $"Temperaturë e ngritur {reading.Temperature:0.0}°C", reading.Temperature));
        }

        // SpO₂: kritik nën 90%, paralajmërim nën 94%.
        if (reading.Spo2 < 90)
        {
            alerts.Add(Build(reading, "SpO2", Critical, $"Saturim i ulët i oksigjenit {reading.Spo2}%", reading.Spo2));
        }
        else if (reading.Spo2 < 94)
        {
            alerts.Add(Build(reading, "SpO2", Warning, $"Saturimi i oksigjenit {reading.Spo2}%", reading.Spo2));
        }

        // Tensioni i gjakut: kritik kur sistolik > 140 ose diastolik > 90.
        if (reading.SystolicBp > 140 || reading.DiastolicBp > 90)
        {
            alerts.Add(Build(reading, "BloodPressure", Critical,
                $"Tension i lartë i gjakut {reading.SystolicBp}/{reading.DiastolicBp} mmHg", reading.SystolicBp));
        }
        else if (reading.SystolicBp > 130 || reading.DiastolicBp > 85)
        {
            alerts.Add(Build(reading, "BloodPressure", Warning,
                $"Tension i gjakut jashtë normës {reading.SystolicBp}/{reading.DiastolicBp} mmHg", reading.SystolicBp));
        }

        // Frymëmarrja: kritike jashtë 10–24/min, paralajmërim në kufi.
        if (reading.RespiratoryRate > 24 || reading.RespiratoryRate < 10)
        {
            alerts.Add(Build(reading, "RespiratoryRate", Critical,
                $"Frekuencë e parregullt e frymëmarrjes {reading.RespiratoryRate}/min", reading.RespiratoryRate));
        }
        else if (reading.RespiratoryRate > 22 || reading.RespiratoryRate < 11)
        {
            alerts.Add(Build(reading, "RespiratoryRate", Warning,
                $"Frekuenca e frymëmarrjes {reading.RespiratoryRate}/min", reading.RespiratoryRate));
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

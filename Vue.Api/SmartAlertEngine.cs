namespace Vue.Api;

// Stateful, "realistic" alert engine for the Smart Health IoT dashboard.
//
// Instead of emitting one notification per measurement (which floods the dashboard),
// it tracks per-patient / per-parameter state and only raises an alert when:
//   1. the value is outside the normal range,
//   2. the breach has persisted for a minimum duration (per parameter),
//   3. the same alert has NOT been raised within the cooldown window
//      (unless the severity got worse, which bypasses the cooldown), and
//   4. it emits a one-off "stabilized" (INFO) notice when a value returns to normal.
//
// Fast vitals (HeartRate, SpO2, RespiratoryRate) react quickly (seconds of persistence);
// slow-moving vitals (Temperature, BloodPressure) are only re-evaluated periodically.
// The dashboard still shows the latest value of every parameter on each reading; this
// engine governs ONLY when a notification is produced.
public sealed class SmartAlertEngine
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";
    public const string Info = "INFO";

    // Per-parameter cadence/persistence rules.
    //  Persistence = how long a breach must last before the FIRST alert fires.
    //  Cooldown    = minimum gap before the SAME alert repeats (severity escalation bypasses it).
    private sealed record Rule(TimeSpan Persistence, TimeSpan Cooldown);

    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, Rule> Rules = new Dictionary<string, Rule>
    {
        ["HeartRate"]       = new(TimeSpan.FromSeconds(15), DefaultCooldown),
        ["SpO2"]            = new(TimeSpan.FromSeconds(10), DefaultCooldown),
        ["RespiratoryRate"] = new(TimeSpan.FromSeconds(20), DefaultCooldown),
        ["Temperature"]     = new(TimeSpan.FromMinutes(1),  DefaultCooldown),
        ["BloodPressure"]   = new(TimeSpan.FromMinutes(5),  DefaultCooldown),
    };

    private sealed class State
    {
        public DateTimeOffset? BreachStart;
        public DateTimeOffset LastAlertAt = DateTimeOffset.MinValue;
        public int LastSeverityRank = -1; // -1 = no active alert
        public bool Active;
    }

    private readonly Dictionary<string, State> _states = new();
    private readonly object _gate = new();

    public IReadOnlyList<AlertMessageDto> Evaluate(VitalReadingDto r)
    {
        var alerts = new List<AlertMessageDto>();
        lock (_gate)
        {
            Apply(r, "HeartRate", ClassifyHeartRate(r.HeartRate), r.HeartRate, alerts);
            Apply(r, "SpO2", ClassifySpo2(r.Spo2), r.Spo2, alerts);
            Apply(r, "RespiratoryRate", ClassifyRespiratory(r.RespiratoryRate), r.RespiratoryRate, alerts);
            Apply(r, "Temperature", ClassifyTemperature(r.Temperature), r.Temperature, alerts);
            Apply(r, "BloodPressure", ClassifyBloodPressure(r.SystolicBp, r.DiastolicBp), r.SystolicBp, alerts);
        }
        return alerts;
    }

    private void Apply(VitalReadingDto r, string type, (string Severity, string Message)? breach, double value, List<AlertMessageDto> sink)
    {
        var rule = Rules[type];
        var key = $"{r.PatientId}|{type}";
        if (!_states.TryGetValue(key, out var st))
        {
            st = new State();
            _states[key] = st;
        }

        var now = r.RecordedAt;

        // Value is back within the normal range.
        if (breach is null)
        {
            st.BreachStart = null;
            if (st.Active)
            {
                st.Active = false;
                st.LastSeverityRank = -1;
                sink.Add(Build(r, type, Info, StabilizedMessage(type, r), value));
            }
            return;
        }

        var (severity, message) = breach.Value;
        var rank = Rank(severity);

        st.BreachStart ??= now;

        var persisted = now - st.BreachStart.Value >= rule.Persistence;
        if (!persisted)
        {
            return; // breach not sustained long enough yet
        }

        var escalated = !st.Active || rank > st.LastSeverityRank;
        var cooldownElapsed = now - st.LastAlertAt >= rule.Cooldown;

        if (escalated || cooldownElapsed)
        {
            st.Active = true;
            st.LastSeverityRank = rank;
            st.LastAlertAt = now;
            sink.Add(Build(r, type, severity, message, value));
        }
    }

    // ---------------- Threshold classifiers (null = normal) ----------------

    private static (string, string)? ClassifyHeartRate(int hr)
    {
        if (hr > 120 || hr < 50) return (Critical, $"Pulsi {(hr > 120 ? "i lartë" : "i ulët")} {hr} BPM");
        return null;
    }

    private static (string, string)? ClassifySpo2(int spo2)
    {
        if (spo2 < 90) return (Critical, $"Saturim kritik i oksigjenit {spo2}%");
        if (spo2 < 92) return (Warning, $"Saturim i ulët i oksigjenit {spo2}%");
        return null;
    }

    private static (string, string)? ClassifyRespiratory(int rr)
    {
        if (rr <= 0) return null;
        if (rr > 24 || rr < 10) return (Critical, $"Frekuencë e parregullt e frymëmarrjes {rr}/min");
        if (rr > 22 || rr < 11) return (Warning, $"Frekuenca e frymëmarrjes {rr}/min");
        return null;
    }

    private static (string, string)? ClassifyTemperature(double t)
    {
        if (t > 38.5) return (Critical, $"Temperaturë e lartë {t:0.0}°C");
        if (t > 38.0) return (Warning, $"Temperaturë e ngritur {t:0.0}°C");
        return null;
    }

    private static (string, string)? ClassifyBloodPressure(int sys, int dia)
    {
        if (sys > 140 || dia > 90) return (Critical, $"Tension i lartë i gjakut {sys}/{dia} mmHg");
        if (sys < 90 || dia < 60) return (Critical, $"Tension i ulët i gjakut {sys}/{dia} mmHg");
        if (sys > 130 || dia > 85) return (Warning, $"Tension i gjakut jashtë normës {sys}/{dia} mmHg");
        return null;
    }

    private static string StabilizedMessage(string type, VitalReadingDto r) => type switch
    {
        "HeartRate" => $"Pulsi u stabilizua ({r.HeartRate} BPM)",
        "SpO2" => $"Saturimi i oksigjenit u stabilizua ({r.Spo2}%)",
        "RespiratoryRate" => $"Frymëmarrja u stabilizua ({r.RespiratoryRate}/min)",
        "Temperature" => $"Temperatura u stabilizua ({r.Temperature:0.0}°C)",
        "BloodPressure" => $"Tensioni u stabilizua ({r.SystolicBp}/{r.DiastolicBp} mmHg)",
        _ => "Parametri u stabilizua"
    };

    private static int Rank(string severity) => severity switch
    {
        Critical => 2,
        Warning => 1,
        _ => 0
    };

    private static AlertMessageDto Build(VitalReadingDto r, string type, string severity, string message, double value) =>
        new(Guid.NewGuid(), r.PatientId, r.RoomNumber, type, severity, message, value,
            r.HeartRate, r.Spo2, r.Temperature, r.SystolicBp, r.DiastolicBp, r.RespiratoryRate, r.RecordedAt);
}

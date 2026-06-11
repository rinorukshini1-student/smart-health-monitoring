// Stateful "realistic" alert engine for the streaming pipeline (Kafka -> Spark -> Cassandra).
//
// Mirrors Vue.Api.SmartAlertEngine: instead of one notification per measurement it only
// raises an alert when a value is out of range, the breach has persisted long enough,
// and the same alert is not inside its cooldown window (severity escalation bypasses it).
// A one-off INFO "stabilized" notice is emitted when a value returns to normal.
//
// Time reference is the reading's RecordedAt so the behaviour is identical for live and
// replayed data.
public sealed class SmartAlertEngine
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";
    public const string Info = "INFO";

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
        public int LastSeverityRank = -1;
        public bool Active;
    }

    private readonly Dictionary<string, State> _states = new();
    private readonly object _gate = new();

    public IReadOnlyList<AlertMessage> Evaluate(VitalReading r)
    {
        var alerts = new List<AlertMessage>();
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

    private void Apply(VitalReading r, string type, (string Severity, string Message)? breach, double value, List<AlertMessage> sink)
    {
        var rule = Rules[type];
        var key = $"{r.PatientId}|{type}";
        if (!_states.TryGetValue(key, out var st))
        {
            st = new State();
            _states[key] = st;
        }

        var now = r.RecordedAt;

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

        if (now - st.BreachStart.Value < rule.Persistence)
        {
            return;
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

    private static string StabilizedMessage(string type, VitalReading r) => type switch
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

    private static AlertMessage Build(VitalReading r, string type, string severity, string message, double value) =>
        new(Guid.NewGuid(), r.PatientId, r.RoomNumber, type, severity, message, value,
            r.HeartRate, r.Spo2, r.Temperature, r.SystolicBp, r.DiastolicBp, r.RespiratoryRate, r.RecordedAt);
}

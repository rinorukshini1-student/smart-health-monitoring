namespace Vue.Api;

// The fixed roster of simulated patients with their clinical profiles (heart-attack dataset features).
public static class PatientCatalog
{
    public static IReadOnlyList<PatientDefinition> Patients { get; } =
    [
        new("P001", "Patient 01", "101", 67, "Male",   238, 1, 1, 1, 1, 1, 3.2,  "Unhealthy", 1, 1, 8, 9.1, 31.3, 286, 1, 6),
        new("P002", "Patient 02", "102", 54, "Male",   190, 0, 0, 1, 0, 1, 5.1,  "Average",   0, 0, 5, 6.0, 27.2, 180, 3, 7),
        new("P003", "Patient 03", "103", 72, "Female", 265, 1, 1, 0, 1, 0, 1.4,  "Unhealthy", 1, 1, 9, 10.2, 33.8, 320, 0, 5),
        new("P004", "Patient 04", "104", 45, "Female", 175, 0, 0, 0, 0, 0, 7.8,  "Healthy",   0, 0, 3, 3.5, 23.1, 120, 5, 8),
        new("P005", "Patient 05", "105", 81, "Male",   290, 1, 1, 1, 1, 1, 0.8,  "Unhealthy", 1, 1, 10, 11.0, 35.6, 410, 0, 4),
        new("P006", "Patient 06", "106", 38, "Male",   168, 0, 0, 0, 0, 0, 8.5,  "Healthy",   0, 0, 2, 2.8, 22.0, 110, 6, 8),
        new("P007", "Patient 07", "107", 60, "Female", 220, 1, 0, 1, 1, 1, 2.6,  "Average",   0, 1, 7, 7.4, 29.9, 240, 2, 6),
        new("P008", "Patient 08", "108", 29, "Female", 160, 0, 0, 0, 0, 0, 9.0,  "Healthy",   0, 0, 1, 2.0, 21.5, 95,  6, 8),
        new("P009", "Patient 09", "109", 75, "Male",   272, 1, 1, 1, 0, 1, 1.1,  "Unhealthy", 1, 1, 9, 9.8, 34.2, 360, 1, 5),
        new("P010", "Patient 10", "110", 50, "Female", 205, 0, 1, 0, 1, 0, 4.4,  "Average",   1, 0, 6, 5.5, 28.4, 210, 3, 7)
    ];
}

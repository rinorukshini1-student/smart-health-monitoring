namespace Vue.Api;

// Lista fikse e pacientëve të simuluar me profile klinike (fushat e dataset-it të infarktit).
public static class PatientCatalog
{
    public static IReadOnlyList<PatientDefinition> Patients { get; } =
    [
        new("P001", "Pacienti 01", "101", 67, "Mashkull", 238, 1, 1, 1, 1, 1, 3.2,  "Jo e shëndetshme", 1, 1, 8, 9.1, 31.3, 286, 1, 6),
        new("P002", "Pacienti 02", "102", 54, "Mashkull", 190, 0, 0, 1, 0, 1, 5.1,  "Mesatare",         0, 0, 5, 6.0, 27.2, 180, 3, 7),
        new("P003", "Pacienti 03", "103", 72, "Femër",    265, 1, 1, 0, 1, 0, 1.4,  "Jo e shëndetshme", 1, 1, 9, 10.2, 33.8, 320, 0, 5),
        new("P004", "Pacienti 04", "104", 45, "Femër",    175, 0, 0, 0, 0, 0, 7.8,  "E shëndetshme",    0, 0, 3, 3.5, 23.1, 120, 5, 8),
        new("P005", "Pacienti 05", "105", 81, "Mashkull", 290, 1, 1, 1, 1, 1, 0.8,  "Jo e shëndetshme", 1, 1, 10, 11.0, 35.6, 410, 0, 4),
        new("P006", "Pacienti 06", "106", 38, "Mashkull", 168, 0, 0, 0, 0, 0, 8.5,  "E shëndetshme",    0, 0, 2, 2.8, 22.0, 110, 6, 8),
        new("P007", "Pacienti 07", "107", 60, "Femër",    220, 1, 0, 1, 1, 1, 2.6,  "Mesatare",         0, 1, 7, 7.4, 29.9, 240, 2, 6),
        new("P008", "Pacienti 08", "108", 29, "Femër",    160, 0, 0, 0, 0, 0, 9.0,  "E shëndetshme",    0, 0, 1, 2.0, 21.5, 95,  6, 8),
        new("P009", "Pacienti 09", "109", 75, "Mashkull", 272, 1, 1, 1, 0, 1, 1.1,  "Jo e shëndetshme", 1, 1, 9, 9.8, 34.2, 360, 1, 5),
        new("P010", "Pacienti 10", "110", 50, "Femër",    205, 0, 1, 0, 1, 0, 4.4,  "Mesatare",         1, 0, 6, 5.5, 28.4, 210, 3, 7)
    ];
}

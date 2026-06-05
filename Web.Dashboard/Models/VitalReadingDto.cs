namespace Web.Dashboard.Models;

public sealed record VitalReadingDto(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int Age,
    int HeartRate,
    int Spo2,
    double Temperature,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    DateTimeOffset RecordedAt);

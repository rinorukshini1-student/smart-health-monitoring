namespace Web.Dashboard.Models;

public sealed record VitalReadingDto(
    string PatientId,
    string PatientName,
    string RoomNumber,
    int HeartRate,
    int Spo2,
    double Temperature,
    DateTimeOffset RecordedAt);

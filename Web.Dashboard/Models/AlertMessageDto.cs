namespace Web.Dashboard.Models;

public sealed record AlertMessageDto(
    Guid AlertId,
    string PatientId,
    string RoomNumber,
    string Severity,
    string Message,
    int HeartRate,
    int Spo2,
    double Temperature,
    DateTimeOffset RecordedAt);

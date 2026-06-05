namespace Web.Dashboard.Models;

public sealed record AlertMessageDto(
    Guid AlertId,
    string PatientId,
    string RoomNumber,
    string AlertType,
    string Severity,
    string Message,
    double Value,
    int HeartRate,
    int Spo2,
    double Temperature,
    int SystolicBp,
    int DiastolicBp,
    int RespiratoryRate,
    DateTimeOffset RecordedAt);

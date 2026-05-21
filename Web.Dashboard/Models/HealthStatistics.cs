namespace Web.Dashboard.Models;

public sealed record HeartRatePoint(DateTimeOffset Timestamp, int HeartRate, string RoomNumber);

public sealed record RoomAlertCount(string RoomNumber, int Count);

public sealed record HealthStatistics(
    IReadOnlyList<HeartRatePoint> HeartRates,
    IReadOnlyList<RoomAlertCount> AlertsByRoom,
    double AverageTemperature);

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.Dashboard.Pages;

public sealed class RoomsModel : PageModel
{
    public IReadOnlyList<RoomCard> Rooms { get; } = Enumerable
        .Range(101, 10)
        .Select(room => new RoomCard(room.ToString(), $"Patient {room - 100:00}"))
        .ToArray();
}

public sealed record RoomCard(string RoomNumber, string PatientName);

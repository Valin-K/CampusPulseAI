namespace EventScheduling.Database.Models;
public enum RoomKind
{
    Study,
    Lab,
    Lecture
}

public sealed class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string Description { get; set; } = string.Empty;
    public RoomKind Kind { get; set; } = RoomKind.Study;
    public bool IsStaffRoom => Kind is RoomKind.Lab or RoomKind.Lecture;

    public void UpdateFrom(RoomRequest request)
    {
        Name = request.Name.Trim();
        Location = request.Location.Trim();
        Capacity = request.Capacity;
        Description = request.Description.Trim();
        Kind = request.Kind;
    }

    public RoomResponse ToResponse() => new(Id, Name, Location, Capacity, Description, Kind);
}

public sealed record RoomRequest(
    string Name,
    string Location,
    int Capacity,
    string Description,
    RoomKind Kind = RoomKind.Study)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120)
        {
            return "Name is required and must be 120 characters or fewer.";
        }

        if (string.IsNullOrWhiteSpace(Location) || Location.Trim().Length > 120)
        {
            return "Location is required and must be 120 characters or fewer.";
        }

        if (Capacity <= 0)
        {
            return "Capacity must be greater than zero.";
        }

        return null;
    }

    public Room ToRoom() => new()
    {
        Name = Name.Trim(),
        Location = Location.Trim(),
        Capacity = Capacity,
        Description = Description.Trim(),
        Kind = Kind
    };
}

public sealed record RoomResponse(
    int Id,
    string Name,
    string Location,
    int Capacity,
    string Description,
    RoomKind Kind);

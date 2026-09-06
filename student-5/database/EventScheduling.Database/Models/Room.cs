namespace EventScheduling.Database.Models;
public enum RoomKind
{
    Study,
    Lab,
    Lecture,
    Collaborative
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
    public bool IsActive { get; set; } = true;

    public string OpeningHours { get; set; } = string.Empty;

    public bool IsAccessible { get; set; }

    public void UpdateFrom(RoomRequest request)
    {
        Name = request.Name.Trim();
        Location = request.Location.Trim();
        Capacity = request.Capacity;
        Description = request.Description.Trim();
        Kind = request.Kind;
        IsActive = request.IsActive;
        OpeningHours = request.OpeningHours.Trim();
        IsAccessible = request.IsAccessible;
    }

    public RoomResponse ToResponse() => new(Id, Name, Location, Capacity, Description, Kind, IsActive, OpeningHours, IsAccessible);
}

public sealed record RoomRequest(
    string Name,
    string Location,
    int Capacity,
    string Description,
    RoomKind Kind = RoomKind.Study,
    bool IsActive = true,
    string OpeningHours = "",
    bool IsAccessible = false)
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

        if (OpeningHours.Length > 100)
        {
            return "Opening hours must be 100 characters or fewer.";
        }

        return null;
    }

    public Room ToRoom() => new()
    {
        Name = Name.Trim(),
        Location = Location.Trim(),
        Capacity = Capacity,
        Description = Description.Trim(),
        Kind = Kind,
        IsActive = IsActive,
        OpeningHours = OpeningHours.Trim(),
        IsAccessible = IsAccessible
    };
}

public sealed record RoomResponse(
    int Id,
    string Name,
    string Location,
    int Capacity,
    string Description,
    RoomKind Kind,
    bool IsActive = true,
    string OpeningHours = "",
    bool IsAccessible = false);

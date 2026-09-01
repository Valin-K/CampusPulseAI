namespace EventScheduling.Frontend.Models;

public enum ResourceType
{
    Room,
    Equipment
}

public enum BookingStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
    Overdue
}

public enum EquipmentStatus
{
    AVL,
    OCC,
    MTC,
    RET
}

public sealed record BookingResponse(
    int Id,
    int StudentId,
    int? RoomId,
    int? EquipmentId,
    int? RefEquipmentId,
    ResourceType ResourceType,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status);

public sealed record RoomResponse(
    int Id,
    string Name,
    string Location,
    int Capacity,
    string Description);

public sealed record RefEquipmentResponse(
    int Id,
    string Name,
    string Category,
    string Description,
    int AvailableCount);

public sealed record EquipmentResponse(
    int Id,
    int RefEquipmentId,
    string SerialNumber,
    EquipmentStatus Status,
    int? CreatedByStaffId);

// A user from the Academic Records (student-1) directory, proxied via /api/users/{id}.
public sealed record UserProfile(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId)
{
    public string FullName => $"{FirstName} {LastName}".Trim();

    public string Initials
    {
        get
        {
            var first = FirstName.Length > 0 ? FirstName[..1] : string.Empty;
            var last = LastName.Length > 0 ? LastName[..1] : string.Empty;
            var initials = (first + last).ToUpperInvariant();
            return initials.Length > 0 ? initials : "?";
        }
    }
}

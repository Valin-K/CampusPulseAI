namespace EventScheduling.Database.Models;

public sealed class Booking
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int? RoomId { get; set; }
    public int? EquipmentId { get; set; }
    public int? RefEquipmentId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public BookingStatus Status { get; set; }

    public void UpdateFrom(BookingRequest request)
    {
        StudentId = request.StudentId;
        RoomId = request.RoomId;
        RefEquipmentId = request.RefEquipmentId;
        ResourceName = request.ResourceName.Trim();
        StartUtc = request.StartUtc;
        EndUtc = request.EndUtc;
        Status = request.Status;
    }

    public BookingResponse ToResponse() => new(
        Id,
        StudentId,
        RoomId,
        EquipmentId,
        RefEquipmentId,
        RoomId.HasValue ? ResourceType.Room : ResourceType.Equipment,
        ResourceName,
        StartUtc,
        EndUtc,
        Status);
}

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
    Completed
}

public sealed record BookingRequest(
    int StudentId,
    int? RoomId,
    int? RefEquipmentId,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status)
{
    public string? Validate()
    {
        if (StudentId <= 0)
        {
            return "Student ID must be greater than zero.";
        }

        if (RoomId.HasValue == RefEquipmentId.HasValue)
        {
            return "Exactly one of room or equipment type must be specified.";
        }

        if (string.IsNullOrWhiteSpace(ResourceName) || ResourceName.Trim().Length > 120)
        {
            return "Resource name is required and must be 120 characters or fewer.";
        }

        if (EndUtc <= StartUtc)
        {
            return "End time must be after the start time.";
        }

        return null;
    }

    public Booking ToBooking() => new()
    {
        StudentId = StudentId,
        RoomId = RoomId,
        RefEquipmentId = RefEquipmentId,
        ResourceName = ResourceName.Trim(),
        StartUtc = StartUtc,
        EndUtc = EndUtc,
        Status = Status
    };
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

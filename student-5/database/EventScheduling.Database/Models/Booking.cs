namespace EventScheduling.Database.Models;

public sealed class Booking
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public ResourceType ResourceType { get; set; }
    public int ResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public BookingStatus Status { get; set; }

    public void UpdateFrom(BookingRequest request)
    {
        StudentId = request.StudentId;
        ResourceType = request.ResourceType;
        ResourceId = request.ResourceId;
        ResourceName = request.ResourceName.Trim();
        StartUtc = request.StartUtc;
        EndUtc = request.EndUtc;
        Status = request.Status;
    }

    public BookingResponse ToResponse() => new(Id, StudentId, ResourceType, ResourceId, ResourceName, StartUtc, EndUtc, Status);
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
    Cancelled
}

public sealed record BookingRequest(
    int StudentId,
    ResourceType ResourceType,
    int ResourceId,
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
        ResourceType = ResourceType,
        ResourceId = ResourceId,
        ResourceName = ResourceName.Trim(),
        StartUtc = StartUtc,
        EndUtc = EndUtc,
        Status = Status
    };
}

public sealed record BookingResponse(
    int Id,
    int StudentId,
    ResourceType ResourceType,
    int ResourceId,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status);

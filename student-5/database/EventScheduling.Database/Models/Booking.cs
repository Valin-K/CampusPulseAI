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
    public int? AttendeeCount { get; set; }
    public string? CheckOutConditionNotes { get; set; }
    public string? ReturnConditionNotes { get; set; }

    public void UpdateFrom(BookingRequest request)
    {
        StudentId = request.StudentId;
        RoomId = request.RoomId;
        RefEquipmentId = request.RefEquipmentId;
        ResourceName = request.ResourceName.Trim();
        StartUtc = request.StartUtc;
        EndUtc = request.EndUtc;
        Status = request.Status;
        AttendeeCount = request.AttendeeCount;
        CheckOutConditionNotes = NormalizeNotes(request.ConditionNotes);
    }

    public static readonly TimeSpan MaxEquipmentHire = TimeSpan.FromDays(7);

    public static readonly TimeSpan MaxRoomBookingDuration = TimeSpan.FromHours(4);

    public static readonly TimeSpan MinBookingNotice = TimeSpan.FromMinutes(30);

    public const int MaxActiveBookingsPerStudent = 120;

    public const int MaxConditionNotesLength = 500;

    internal static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public BookingResponse ToResponse()
    {
        var status = Status;

        if (status is BookingStatus.Pending or BookingStatus.Confirmed
            && EquipmentId is not null
            && DateTime.UtcNow > EndUtc)
        {
            status = BookingStatus.Overdue;
        }

        return new(
            Id,
            StudentId,
            RoomId,
            EquipmentId,
            RefEquipmentId,
            RoomId.HasValue ? ResourceType.Room : ResourceType.Equipment,
            ResourceName,
            StartUtc,
            EndUtc,
            status,
            AttendeeCount,
            CheckOutConditionNotes,
            ReturnConditionNotes);
    }
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
    Completed,
    Overdue,
    Returned
}

public sealed record BookingRequest(
    int StudentId,
    int? RoomId,
    int? RefEquipmentId,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status,
    bool IsStaff = false,
    int? AttendeeCount = null,
    string? ConditionNotes = null)
{
    public string? Validate()
    {
        if (StudentId <= 0)
        {
            return "Student ID must be greater than zero.";
        }

        if (Status is BookingStatus.Overdue or BookingStatus.Completed or BookingStatus.Returned)
        {
            return "Overdue, Completed and Returned are managed by the system and cannot be set directly.";
        }

        if (!RoomId.HasValue && !RefEquipmentId.HasValue)
        {
            return "At least one of room or equipment type must be specified.";
        }

        if (string.IsNullOrWhiteSpace(ResourceName) || ResourceName.Trim().Length > 120)
        {
            return "Resource name is required and must be 120 characters or fewer.";
        }

        if (EndUtc <= StartUtc)
        {
            return "End time must be after the start time.";
        }

        if (RefEquipmentId.HasValue && EndUtc - StartUtc > Booking.MaxEquipmentHire)
        {
            return "Equipment can be hired for a maximum of 7 days.";
        }

        if (RoomId.HasValue && EndUtc - StartUtc > Booking.MaxRoomBookingDuration)
        {
            return $"A single room booking can run for at most {Booking.MaxRoomBookingDuration.TotalHours:0.#} hours.";
        }

        if (AttendeeCount is <= 0)
        {
            return "Attendee count must be greater than zero.";
        }

        if (!RoomId.HasValue && AttendeeCount.HasValue)
        {
            return "Attendee count only applies to room bookings.";
        }

        if (ConditionNotes is { Length: > Booking.MaxConditionNotesLength })
        {
            return $"Condition notes must be {Booking.MaxConditionNotesLength} characters or fewer.";
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
        Status = Status,
        AttendeeCount = AttendeeCount,
        CheckOutConditionNotes = Booking.NormalizeNotes(ConditionNotes)
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
    BookingStatus Status,
    int? AttendeeCount = null,
    string? CheckOutConditionNotes = null,
    string? ReturnConditionNotes = null);

public sealed record ReturnRequest(string? ConditionNotes)
{
    public string? Validate() =>
        ConditionNotes is { Length: > Booking.MaxConditionNotesLength }
            ? $"Condition notes must be {Booking.MaxConditionNotesLength} characters or fewer."
            : null;
}

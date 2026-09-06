namespace EventScheduling.Frontend.Models;

public enum ResourceType
{
    Room,
    Equipment
}

public enum RoomKind
{
    Study,
    Lab,
    Lecture
}

public enum BookingStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
    Overdue,
    Returned,
    InUse
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
    BookingStatus Status,
    int? AttendeeCount = null,
    string? CheckOutConditionNotes = null,
    string? ReturnConditionNotes = null);

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
    string? ConditionNotes = null);

// FR-21: an optional note on the equipment's condition, taken at return time.
public sealed record ReturnRequest(string? ConditionNotes);

public sealed record BookingResult(bool Success, BookingResponse? Booking, string? Error);

public sealed record RecurringBookingResult(int Created, int Total, IReadOnlyList<string> Failures)
{
    public bool AllCreated => Created == Total && Total > 0;
}

public static class BookingStatusView
{
    public static BookingStatus DisplayStatus(this BookingResponse booking)
    {
        var now = DateTime.Now;

        if (booking.Status is BookingStatus.Confirmed
            && booking.ResourceType == ResourceType.Equipment
            && booking.EquipmentId is not null
            && now >= booking.StartUtc && now < booking.EndUtc)
        {
            return BookingStatus.InUse;
        }

        if (booking.Status is BookingStatus.Pending or BookingStatus.Confirmed && now > booking.EndUtc)
        {
            return BookingStatus.Completed;
        }

        return booking.Status;
    }

    public static string Label(this BookingStatus status) =>
        status == BookingStatus.InUse ? "In use" : status.ToString();

    public static string CssName(this BookingStatus status) =>
        status.ToString().ToLowerInvariant();
}

public sealed record RoomResponse(
    int Id,
    string Name,
    string Location,
    int Capacity,
    string Description,
    RoomKind Kind = RoomKind.Study,
    bool IsActive = true,
    string OpeningHours = "")
{
    public bool IsStaffRoom => Kind is RoomKind.Lab or RoomKind.Lecture;

    public string KindLabel => Kind switch
    {
        RoomKind.Lab => "Lab room",
        RoomKind.Lecture => "Lecture room",
        _ => "Study room"
    };
}

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

    public string Descriptor =>
        StudentId is { } studentId ? $"Student {studentId}"
        : StaffId is { } staffId ? $"Staff {staffId}"
        : Role;

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

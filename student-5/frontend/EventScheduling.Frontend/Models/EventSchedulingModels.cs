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
    Completed
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

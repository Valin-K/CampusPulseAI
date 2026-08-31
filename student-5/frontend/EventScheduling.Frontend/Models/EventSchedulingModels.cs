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
    Cancelled
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

public sealed record RoomResponse(
    int Id,
    string Name,
    string Location,
    int Capacity,
    string Description);

public sealed record EquipmentResponse(
    int Id,
    string Name,
    string Category,
    string Description,
    int QuantityAvailable);

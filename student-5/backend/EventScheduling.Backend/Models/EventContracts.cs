using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Models;

public enum RoomKind
{
    Study,
    Lab,
    Lecture
}

public sealed record RoomRequest(
    string Name,
    string Location,
    int Capacity,
    string Description,
    RoomKind Kind = RoomKind.Study);

public sealed record RoomResponse(
    int Id,
    string Name,
    string Location,
    int Capacity,
    string Description,
    RoomKind Kind);

public sealed record RefEquipmentRequest(
    string Name,
    string Category,
    string Description);

public sealed record RefEquipmentResponse(
    int Id,
    string Name,
    string Category,
    string Description,
    int AvailableCount);

public enum EquipmentStatus
{
    AVL,
    OCC,
    MTC,
    RET
}

public sealed record EquipmentRequest(
    int RefEquipmentId,
    string SerialNumber,
    EquipmentStatus Status,
    int? CreatedByStaffId);

public sealed record EquipmentResponse(
    int Id,
    int RefEquipmentId,
    string SerialNumber,
    EquipmentStatus Status,
    int? CreatedByStaffId);

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
    string? ConditionNotes = null);

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

public sealed record ReturnRequest(string? ConditionNotes);

public static class HttpResponseMessageExtensions
{
    public static async Task<IActionResult> ToActionResultAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrEmpty(content))
            {
                return new StatusCodeResult((int)response.StatusCode);
            }

            return new ContentResult
            {
                Content = content,
                ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/json",
                StatusCode = (int)response.StatusCode
            };
        }
    }
}

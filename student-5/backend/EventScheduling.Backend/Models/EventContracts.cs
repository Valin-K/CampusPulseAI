using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Models;

public sealed record RoomRequest(
    string Name,
    string Location,
    int Capacity,
    string Description);

public sealed record RoomResponse(
    int Id,
    string Name,
    string Location,
    int Capacity,
    string Description);

public sealed record EquipmentRequest(
    string Name,
    string Category,
    string Description,
    int QuantityAvailable);

public sealed record EquipmentResponse(
    int Id,
    string Name,
    string Category,
    string Description,
    int QuantityAvailable);

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
    BookingStatus Status);

public sealed record BookingResponse(
    int Id,
    int StudentId,
    ResourceType ResourceType,
    int ResourceId,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status);

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

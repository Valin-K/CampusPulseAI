using System.Net.Http.Json;
using EventScheduling.Frontend.Models;

namespace EventScheduling.Frontend.Services;

public sealed class EventSchedulingApiClient(HttpClient httpClient)
{
    public async Task<List<BookingResponse>> GetBookingsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<BookingResponse>>("/api/bookings", cancellationToken) ?? [];

    public async Task<List<RoomResponse>> GetRoomsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<RoomResponse>>("/api/rooms", cancellationToken) ?? [];

    public async Task<RoomResponse?> GetRoomAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<RoomResponse>($"/api/rooms/{id}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<List<RefEquipmentResponse>> GetRefEquipmentAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<RefEquipmentResponse>>("/api/refequipment", cancellationToken) ?? [];

    public async Task<RefEquipmentResponse?> GetRefEquipmentAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<RefEquipmentResponse>($"/api/refequipment/{id}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<List<EquipmentResponse>> GetEquipmentAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<EquipmentResponse>>("/api/equipment", cancellationToken) ?? [];

    public async Task<UserProfile?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<UserProfile>($"/api/users/{userId}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<BookingResult> CreateBookingAsync(BookingRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/bookings", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var booking = await response.Content.ReadFromJsonAsync<BookingResponse>(cancellationToken);
                return new BookingResult(true, booking, null);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            var message = problem?.Errors?.Values.SelectMany(messages => messages).FirstOrDefault()
                ?? problem?.Detail
                ?? "The booking could not be created.";
            return new BookingResult(false, null, message);
        }
        catch (HttpRequestException)
        {
            return new BookingResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    private sealed record ProblemResponse(string? Detail, Dictionary<string, string[]>? Errors);
}

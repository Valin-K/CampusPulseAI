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

    // Equipment return: unit is freed, booking status becomes Returned, record is kept.
    public Task<BookingResult> ReturnBookingAsync(int id, CancellationToken cancellationToken = default) =>
        PostBookingActionAsync($"/api/bookings/{id}/return", cancellationToken);

    // Room end: the room is freed for that time, booking status becomes Completed, record is kept.
    public Task<BookingResult> EndBookingAsync(int id, CancellationToken cancellationToken = default) =>
        PostBookingActionAsync($"/api/bookings/{id}/end", cancellationToken);

    // Cancel: the booking is deleted outright and any held equipment unit is freed.
    public async Task<BookingResult> CancelBookingAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"/api/bookings/{id}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new BookingResult(true, null, null);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            return new BookingResult(false, null, FirstError(problem) ?? "The booking could not be cancelled.");
        }
        catch (HttpRequestException)
        {
            return new BookingResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    private async Task<BookingResult> PostBookingActionAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.PostAsync(url, content: null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var booking = await response.Content.ReadFromJsonAsync<BookingResponse>(cancellationToken);
                return new BookingResult(true, booking, null);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            return new BookingResult(false, null, FirstError(problem) ?? "The action could not be completed.");
        }
        catch (HttpRequestException)
        {
            return new BookingResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    private static string? FirstError(ProblemResponse? problem) =>
        problem?.Errors?.Values.SelectMany(messages => messages).FirstOrDefault() ?? problem?.Detail;

    private sealed record ProblemResponse(string? Detail, Dictionary<string, string[]>? Errors);
}

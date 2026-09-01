using System.Net.Http.Json;
using EventScheduling.Frontend.Models;

namespace EventScheduling.Frontend.Services;

public sealed class EventSchedulingApiClient(HttpClient httpClient)
{
    public async Task<List<BookingResponse>> GetBookingsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<BookingResponse>>("/api/bookings", cancellationToken) ?? [];

    public async Task<List<RoomResponse>> GetRoomsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<RoomResponse>>("/api/rooms", cancellationToken) ?? [];

    public async Task<List<RefEquipmentResponse>> GetRefEquipmentAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<RefEquipmentResponse>>("/api/refequipment", cancellationToken) ?? [];

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
}

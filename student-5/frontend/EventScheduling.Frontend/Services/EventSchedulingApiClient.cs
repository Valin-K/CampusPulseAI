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

    public async Task<List<RefEquipmentResponse>> GetRefEquipmentAsync(
        DateTime availableFromUtc, DateTime availableToUtc, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<List<RefEquipmentResponse>>(
            $"/api/refequipment?availableFrom={availableFromUtc:O}&availableTo={availableToUtc:O}", cancellationToken) ?? [];

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

    public async Task<List<UserProfile>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<List<UserProfile>>("/api/users", cancellationToken) ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
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
            return new BookingResult(false, null, FormatError(problem) ?? "The booking could not be created.");
        }
        catch (HttpRequestException)
        {
            return new BookingResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    public async Task<RecurringBookingResult> CreateBookingsAsync(
        IReadOnlyList<BookingRequest> requests, CancellationToken cancellationToken = default)
    {
        var created = 0;
        var failures = new List<string>();

        foreach (var request in requests)
        {
            var result = await CreateBookingAsync(request, cancellationToken);
            if (result.Success)
            {
                created++;
            }
            else
            {
                failures.Add($"{request.StartUtc:ddd MMM d} · {result.Error}");
            }
        }

        return new RecurringBookingResult(created, requests.Count, failures);
    }

    public async Task<BookingResult> ReturnBookingAsync(int id, string? conditionNotes = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"/api/bookings/{id}/return", new ReturnRequest(conditionNotes), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var booking = await response.Content.ReadFromJsonAsync<BookingResponse>(cancellationToken);
                return new BookingResult(true, booking, null);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            return new BookingResult(false, null, FormatError(problem) ?? "The return could not be recorded.");
        }
        catch (HttpRequestException)
        {
            return new BookingResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    public Task<BookingResult> EndBookingAsync(int id, CancellationToken cancellationToken = default) =>
        PostBookingActionAsync($"/api/bookings/{id}/end", cancellationToken);

    public async Task<BookingResult> CancelBookingAsync(BookingResponse booking, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new BookingRequest(
                booking.StudentId,
                booking.RoomId,
                booking.RefEquipmentId,
                booking.ResourceName,
                booking.StartUtc,
                booking.EndUtc,
                BookingStatus.Cancelled,
                AttendeeCount: booking.AttendeeCount,
                ConditionNotes: booking.CheckOutConditionNotes);

            var response = await httpClient.PutAsJsonAsync($"/api/bookings/{booking.Id}", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new BookingResult(true, null, null);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            return new BookingResult(false, null, FormatError(problem) ?? "The booking could not be cancelled.");
        }
        catch (HttpRequestException)
        {
            return new BookingResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    public async Task<AssistantResult> AskAssistantAsync(
        AssistantChatRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/ai/assistant", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var chat = await response.Content.ReadFromJsonAsync<AssistantChatResponse>(cancellationToken);
                return chat is null
                    ? new AssistantResult(false, null, "The assistant returned an empty response.")
                    : new AssistantResult(true, chat, null);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            return new AssistantResult(false, null,
                FormatError(problem) ?? "The assistant could not answer right now.");
        }
        catch (HttpRequestException)
        {
            return new AssistantResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    public async Task<McpVerifyResult> VerifyMcpIntegrationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsync("/api/ai/mcp/integration", content: null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<McpIntegrationResponse>(cancellationToken);
                return payload is null
                    ? new McpVerifyResult(false, null, "The MCP server returned an empty response.")
                    : new McpVerifyResult(true, payload, null);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            return new McpVerifyResult(false, null, FormatError(problem) ?? "The MCP integration check failed.");
        }
        catch (HttpRequestException)
        {
            return new McpVerifyResult(false, null, "The Event Scheduling service is unavailable.");
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
            return new BookingResult(false, null, FormatError(problem) ?? "The action could not be completed.");
        }
        catch (HttpRequestException)
        {
            return new BookingResult(false, null, "The Event Scheduling service is unavailable.");
        }
    }

    private static string? FormatError(ProblemResponse? problem)
    {
        var message = problem?.Errors?.Values.SelectMany(messages => messages).FirstOrDefault() ?? problem?.Detail;
        if (message is null)
        {
            return null;
        }

        var label = problem?.Category switch
        {
            "Clash" => "Scheduling conflict",
            "RuleBreach" => "Booking rule",
            "Outage" => "Service unavailable",
            _ => null
        };

        return label is null ? message : $"{label}: {message}";
    }

    private sealed record ProblemResponse(string? Detail, Dictionary<string, string[]>? Errors, string? Category);
}

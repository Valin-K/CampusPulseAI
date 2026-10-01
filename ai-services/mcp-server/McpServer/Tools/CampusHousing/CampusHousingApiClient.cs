using System.Net.Http.Json;
using McpServer.Contracts;

namespace McpServer.Tools.CampusHousing;

public sealed class CampusHousingApiClient(HttpClient campusHousing) : IDisposable
{
    public void Dispose() => campusHousing.Dispose();

    public async Task<McpToolExecution> GetRoomStatusAsync(CancellationToken cancellationToken)
    {
        var rooms = await campusHousing.GetFromJsonAsync<List<DormRoomStatus>>(
            "/api/rooms", cancellationToken) ?? [];
        var availableRooms = rooms
            .Where(room => room.IsAvailable && room.OccupiedBeds < room.Capacity)
            .Select(room => new
            {
                room.Building,
                room.RoomNumber,
                room.Capacity,
                room.OccupiedBeds,
                openBeds = Math.Max(0, room.Capacity - room.OccupiedBeds),
                available = true
            })
            .ToList();

        return new McpToolExecution(false, new
        {
            availableRooms,
            count = availableRooms.Count
        });
    }

    public async Task<McpToolExecution> SearchRelatedTicketsAsync(
        string description,
        CancellationToken cancellationToken)
    {
        var tickets = await campusHousing.GetFromJsonAsync<List<MaintenanceTicket>>(
            "/api/tickets", cancellationToken) ?? [];
        var requestTokens = Tokens(description);
        var matches = tickets
            .Where(ticket => !string.Equals(ticket.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
            .Select(ticket => (Ticket: ticket, Score: Similarity(requestTokens, Tokens(ticket.Description))))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Ticket.CreatedAt)
            .Take(5)
            .Select(item => new
            {
                description = item.Ticket.Description,
                severity = item.Ticket.Severity,
                repairQueue = item.Ticket.RepairQueue,
                status = item.Ticket.Status,
                similarity = Math.Round(item.Score, 4)
            })
            .ToList();

        return new McpToolExecution(false, new
        {
            relatedOpenTickets = matches,
            count = matches.Count
        });
    }

    private static HashSet<string> Tokens(string value) => value
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
        .Where(token => token.Length >= 3)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static double Similarity(IReadOnlySet<string> requestTokens, IReadOnlySet<string> ticketTokens) =>
        requestTokens.Count == 0 || ticketTokens.Count == 0
            ? 0
            : requestTokens.Intersect(ticketTokens, StringComparer.OrdinalIgnoreCase).Count() / (double)requestTokens.Count;

    private sealed record MaintenanceTicket(
        int Id,
        string StudentId,
        int? DormRoomId,
        string Description,
        string Severity,
        string RepairQueue,
        string Status,
        DateTime CreatedAt,
        DateTime? ResolvedAt);

    private sealed record DormRoomStatus(
        int Id,
        string Building,
        string RoomNumber,
        int Capacity,
        int OccupiedBeds,
        bool IsAvailable);
}

using System.Globalization;
using System.Net.Http.Json;
using CampusHousing.Backend.Models;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;

namespace CampusHousing.Backend.Services;

public sealed class HousingKnowledgeService(
    IHttpClientFactory clients,
    RagKnowledgeService knowledge,
    ILogger<HousingKnowledgeService> logger) : IHousingRagService
{
    private static readonly RagFeature Feature = RagFeature.CampusHousing;

    public async Task<RagAssistantAnswer> AskAsync(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        var corpus = await BuildCorpusAsync(cancellationToken);

        return await knowledge.AskAsync(
            Feature,
            request.Question,
            corpus,
            cancellationToken);
    }

    private async Task<IReadOnlyList<RagDocument>> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        var documents = new List<RagDocument>(PolicyDocuments);

        try
        {
            var rooms = await clients.CreateClient("CampusHousingDatabase")
                .GetFromJsonAsync<List<DormRoom>>("api/rooms", cancellationToken);
            documents.AddRange(rooms?.Select(RoomDocument) ?? []);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not load dorm rooms for the knowledge corpus.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading dorm rooms for the knowledge corpus.");
        }

        return documents;
    }

    private static RagDocument RoomDocument(DormRoom room)
    {
        var culture = CultureInfo.InvariantCulture;
        var availableBeds = Math.Max(0, room.Capacity - room.OccupiedBeds);
        var isAvailable = room.IsAvailable && availableBeds > 0;
        var text =
            $"Room {room.RoomNumber} in {room.Building} houses up to {room.Capacity.ToString(culture)} residents. "
            + $"It currently has {room.OccupiedBeds.ToString(culture)} occupied bed(s) and "
            + $"{availableBeds.ToString(culture)} open bed(s). "
            + $"It is currently {(isAvailable ? "available for allocation" : "not available for allocation")}.";
        return new RagDocument(
            $"room-{room.Id.ToString(culture)}",
            text,
            new Dictionary<string, string> { ["title"] = $"{room.Building} {room.RoomNumber}" });
    }

    private static readonly IReadOnlyList<RagDocument> PolicyDocuments =
    [
        new(
            "policy-allocation",
            "Housing allocation. Students are allocated to dorm rooms within campus buildings. Each room has a "
            + "capacity and can hold residents up to that capacity. Only rooms that are marked available can be "
            + "allocated.",
            new Dictionary<string, string> { ["title"] = "Housing allocation" }),
        new(
            "policy-maintenance",
            "Maintenance requests. A resident can raise a maintenance ticket describing a problem with their room. "
            + "Each ticket is assigned a severity and routed to a repair queue, then progresses through statuses "
            + "until it is resolved.",
            new Dictionary<string, string> { ["title"] = "Maintenance requests" }),
        new(
            "policy-rooms",
            "Dorm rooms. Every dorm room belongs to a building and has a room number and a capacity. A room is "
            + "either available for allocation or already fully allocated.",
            new Dictionary<string, string> { ["title"] = "Dorm rooms" }),
    ];
}

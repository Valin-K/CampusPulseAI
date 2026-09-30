using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using CampusHousing.Backend.Models;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;

namespace CampusHousing.Backend.Services;

public sealed class HousingKnowledgeService(
    IHttpClientFactory clients,
    IMcpClient mcp,
    RagKnowledgeService knowledge,
    ILogger<HousingKnowledgeService> logger) : IHousingKnowledgeService
{
    private static readonly RagFeature Feature = RagFeature.CampusHousing;

    public async Task<RagAssistantAnswer> AskAsync(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        var corpus = await BuildCorpusAsync(cancellationToken);
        var mcpContext = await BuildMcpContextAsync(cancellationToken);

        // AGENT-MIGRATION: handing the corpus to the shared RAG assistant (ask + citation parsing)
        // moves into the multi-agent server; building this feature's corpus stays here because this
        // feature owns its data.
        var answer = await knowledge.AskAsync(
            Feature,
            request.Question,
            corpus,
            cancellationToken,
            mcpContext?.Context);
        if (mcpContext is null)
        {
            return answer;
        }

        var sources = answer.Sources.ToList();
        sources.Add(new RagKnowledgeSource(
            sources.Count + 1,
            mcpContext.SourceLabel,
            "mcp:validate_integration",
            1));

        return answer with { Sources = sources };
    }

    private async Task<McpContext?> BuildMcpContextAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await mcp.CallAsync(
                "validate_integration",
                new Dictionary<string, JsonElement>
                {
                    ["feature"] = JsonSerializer.SerializeToElement("student-3"),
                },
                cancellationToken);

            return new McpContext(
                $"Shared MCP integration context for Campus Housing: {result.Result}",
                "Shared MCP integration validation");
        }
        catch (McpResponseException exception)
        {
            logger.LogWarning(exception, "Could not load shared MCP context for the housing assistant.");
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading shared MCP context for the housing assistant.");
            return null;
        }
    }

    private sealed record McpContext(string Context, string SourceLabel);

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
        var text =
            $"Room {room.RoomNumber} in {room.Building} houses up to {room.Capacity.ToString(culture)} residents. "
            + $"It is currently {(room.IsAvailable ? "available for allocation" : "fully allocated")}.";
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

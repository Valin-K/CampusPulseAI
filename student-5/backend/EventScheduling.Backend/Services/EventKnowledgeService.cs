using System.Globalization;
using System.Net.Http.Json;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using EventScheduling.Backend.Clients;
using EventScheduling.Backend.Models;

namespace EventScheduling.Backend.Services;

public sealed class EventKnowledgeService(
    IHttpClientFactory clients,
    IRagServerClient rag,
    RagCitationParser citationParser,
    ILogger<EventKnowledgeService> logger) : IEventKnowledgeService
{
    private static readonly RagFeature Feature = RagFeature.EventScheduling;

    public async Task<KnowledgeAnswerResponse> AskAsync(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        var documents = await BuildCorpusAsync(cancellationToken);
        await rag.IngestAsync(Feature.Collection, new RagIngestRequest(documents), cancellationToken);

        // AGENT-MIGRATION: asking the RAG server, parsing its citations, and shaping the reply
        // move into the multi-agent server. Corpus build and ingest stay here because this
        // feature owns its data.
        var response = await rag.AnswerAsync(
            Feature.Collection,
            new RagAnswerRequest(request.Question.Trim(), TopK: 4, Instructions: Feature.Persona),
            cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException("The RAG server returned an empty response.");
        }

        var parsed = citationParser.Parse(response.Answer, response.Sources);
        return Shape(request.Question.Trim(), response, parsed);
    }

    private static KnowledgeAnswerResponse Shape(
        string question, RagAnswerResponse response, ParsedRagAnswer parsed)
    {
        var reply = response.Status switch
        {
            RagStatus.Answered => CitationRenderer.ToText(parsed),
            RagStatus.InsufficientContext => response.Answer
                ?? "I do not have enough information in the knowledge base to answer that.",
            RagStatus.GenerationUnavailable =>
                "Answer generation is not configured on the server, but I retrieved the relevant passages below.",
            _ => "I could not generate an answer just now. The passages below are what I retrieved.",
        };

        var sources = response.Status == RagStatus.Answered
            ? parsed.Cited.Select(cited => new KnowledgeSource(
                cited.DisplayNumber, Label(cited.Source), cited.Source.DocumentId, cited.Source.Score))
            : response.Sources.Select(source => new KnowledgeSource(
                source.Rank, Label(source), source.DocumentId, source.Score));

        return new KnowledgeAnswerResponse(
            question, response.Status, reply, response.Confidence, sources.ToList(), response.Embedder);
    }

    private static string Label(RagSource source) =>
        source.Metadata?.TryGetValue("title", out var title) == true && !string.IsNullOrWhiteSpace(title)
            ? title
            : source.DocumentId;

    private async Task<IReadOnlyList<RagDocument>> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        var database = clients.CreateClient("eventDatabase");
        var roomsTask = database.GetFromJsonAsync<List<RoomResponse>>("/api/rooms", cancellationToken);
        var equipmentTask = database.GetFromJsonAsync<List<RefEquipmentResponse>>("/api/refequipment", cancellationToken);

        try
        {
            await Task.WhenAll(roomsTask, equipmentTask);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not load facility data for the knowledge corpus.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading facility data for the knowledge corpus.");
        }

        var documents = new List<RagDocument>(PolicyDocuments);
        documents.AddRange((roomsTask.IsCompletedSuccessfully ? roomsTask.Result : null)?.Select(RoomDocument) ?? []);
        documents.AddRange((equipmentTask.IsCompletedSuccessfully ? equipmentTask.Result : null)?.Select(EquipmentDocument) ?? []);
        return documents;
    }

    private static RagDocument RoomDocument(RoomResponse room)
    {
        var culture = CultureInfo.InvariantCulture;
        var text =
            $"Room {room.Name} is a {room.Kind} room in {room.Location} seating {room.Capacity}. "
            + $"{room.Description} "
            + $"It is {(room.IsActive ? "active and bookable" : "currently inactive")}. "
            + $"{(room.IsAccessible ? "It is wheelchair accessible. " : string.Empty)}"
            + (string.IsNullOrWhiteSpace(room.OpeningHours) ? string.Empty : $"Opening hours: {room.OpeningHours}.");
        return new RagDocument(
            $"room-{room.Id.ToString(culture)}",
            text,
            new Dictionary<string, string> { ["title"] = room.Name, ["kind"] = room.Kind.ToString() });
    }

    private static RagDocument EquipmentDocument(RefEquipmentResponse item)
    {
        var culture = CultureInfo.InvariantCulture;
        var text =
            $"{item.Name} ({item.Category}): {item.Description} "
            + $"There are {item.AvailableCount.ToString(culture)} units available to hire.";
        return new RagDocument(
            $"equipment-{item.Id.ToString(culture)}",
            text,
            new Dictionary<string, string> { ["title"] = item.Name, ["category"] = item.Category });
    }

    private static readonly IReadOnlyList<RagDocument> PolicyDocuments =
    [
        new(
            "policy-room-eligibility",
            "Room booking eligibility. Students may book Study rooms only. Teaching staff may book "
            + "Lab and Lecture rooms only. Only active rooms can be booked.",
            new Dictionary<string, string> { ["title"] = "Room booking eligibility" }),
        new(
            "policy-room-booking",
            "Room booking rules. A room booking has a start time and an end time on a single day. "
            + "A booking can never start in the past.",
            new Dictionary<string, string> { ["title"] = "Room booking rules" }),
        new(
            "policy-equipment-hire",
            "Equipment hire rules. An equipment hire lasts at most seven days from pickup; the return "
            + "date is the pickup date plus seven days. Only equipment with available units can be hired.",
            new Dictionary<string, string> { ["title"] = "Equipment hire rules" }),
        new(
            "policy-booking-confirmation",
            "Booking confirmation. The assistant proposes a booking with a specific resource, date and "
            + "time; the user confirms it in the app. Bookings are never created automatically.",
            new Dictionary<string, string> { ["title"] = "Booking confirmation" }),
    ];
}

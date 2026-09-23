using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using EventScheduling.Backend.Clients;
using EventScheduling.Backend.Models;

namespace EventScheduling.Backend.Services;

public sealed class EventKnowledgeService(IRagServerClient rag) : IEventKnowledgeService
{
    private static readonly RagFeature Feature = RagFeature.EventScheduling;

    public async Task<RagAnswerResponse> AskAsync(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        var documents = await BuildCorpusAsync(cancellationToken);
        await rag.IngestAsync(Feature.Collection, new RagIngestRequest(documents), cancellationToken);

        // AGENT-MIGRATION: asking the RAG server and interpreting its answer (persona, and the
        // citation parsing added in Stage 4) move into the multi-agent server. Corpus build and
        // ingest stay here because this feature owns its data.
        var response = await rag.AnswerAsync(
            Feature.Collection,
            new RagAnswerRequest(request.Question.Trim(), TopK: 4, Instructions: Feature.Persona),
            cancellationToken);

        return response ?? throw new InvalidOperationException("The RAG server returned an empty response.");
    }

    private static Task<IReadOnlyList<RagDocument>> BuildCorpusAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RagDocument>>(PolicyDocuments);

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

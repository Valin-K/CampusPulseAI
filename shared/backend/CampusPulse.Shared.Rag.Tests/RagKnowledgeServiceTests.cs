using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;

namespace CampusPulse.Shared.Rag.Tests;

public sealed class RagKnowledgeServiceTests
{
    private static readonly RagFeature Feature = RagFeature.EventScheduling;

    private static readonly IReadOnlyList<RagDocument> Corpus =
        [new("policy-equipment-hire", "Equipment hire lasts seven days.", null)];

    private sealed class FakeRagServerClient : IRagServerClient
    {
        public RagIngestRequest? LastIngest { get; private set; }
        public RagAnswerRequest? LastAnswer { get; private set; }
        public required RagAnswerResponse? AnswerResult { get; init; }

        public Task<RagIngestResponse?> IngestAsync(string collection, RagIngestRequest request, CancellationToken cancellationToken)
        {
            LastIngest = request;
            return Task.FromResult<RagIngestResponse?>(new RagIngestResponse(collection, request.Documents.Count, request.Documents.Count, "test-embedder"));
        }

        public Task<RagAnswerResponse?> AnswerAsync(string collection, RagAnswerRequest request, CancellationToken cancellationToken)
        {
            LastAnswer = request;
            return Task.FromResult(AnswerResult);
        }
    }

    private static RagAnswerResponse Response(
        string status,
        string? answer,
        IReadOnlyList<RagSource> sources) =>
        new("eventscheduling", "How long can I hire equipment for?", status, answer, 0.8, sources, "gemini", null, DateTimeOffset.UtcNow);

    private static IReadOnlyList<RagSource> TwoSources() =>
    [
        new(1, "policy-equipment-hire", 0, 0.8, "Equipment hire lasts seven days.", new Dictionary<string, string> { ["title"] = "Equipment hire rules" }),
        new(2, "policy-room", 0, 0.6, "Room rules.", new Dictionary<string, string> { ["title"] = "Room rules" }),
    ];

    [Test]
    public async Task AskAsync_Answered_ReturnsRenderedReplyAndOnlyCitedSources()
    {
        var client = new FakeRagServerClient
        {
            AnswerResult = Response(RagStatus.Answered, "Hire lasts seven days [1].", TwoSources()),
        };
        var service = new RagKnowledgeService(client, new RagCitationParser());

        var result = await service.AskAsync(Feature, "How long can I hire equipment for?", Corpus, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RagStatus.Answered));
            Assert.That(result.Reply, Is.EqualTo("Hire lasts seven days [1]."));
            Assert.That(result.ConfidenceCategory, Is.EqualTo(RagConfidenceCategory.High));
            Assert.That(result.Sources, Has.Count.EqualTo(1));
            Assert.That(result.Sources[0].Number, Is.EqualTo(1));
            Assert.That(result.Sources[0].Label, Is.EqualTo("Equipment hire rules"));
            Assert.That(result.Sources[0].DocumentId, Is.EqualTo("policy-equipment-hire"));
        });
    }

    [Test]
    public async Task AskAsync_SendsThePersonaAndTopKToTheServer()
    {
        var client = new FakeRagServerClient
        {
            AnswerResult = Response(RagStatus.Answered, "Answer [1].", TwoSources()),
        };
        var service = new RagKnowledgeService(client, new RagCitationParser());

        await service.AskAsync(Feature, "  How long can I hire equipment for?  ", Corpus, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.LastIngest!.Documents, Has.Count.EqualTo(1));
            Assert.That(client.LastAnswer!.Instructions, Is.EqualTo(Feature.Persona));
            Assert.That(client.LastAnswer!.TopK, Is.EqualTo(4));
            Assert.That(client.LastAnswer!.Question, Is.EqualTo("How long can I hire equipment for?"));
        });
    }

    [Test]
    public async Task AskAsync_InsufficientContext_FallsBackAndReturnsEveryRetrievedSource()
    {
        var client = new FakeRagServerClient
        {
            AnswerResult = Response(RagStatus.InsufficientContext, null, TwoSources()),
        };
        var service = new RagKnowledgeService(client, new RagCitationParser());

        var result = await service.AskAsync(Feature, "What is the wifi password?", Corpus, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Reply, Is.EqualTo("I do not have enough information in the knowledge base to answer that."));
            Assert.That(result.ConfidenceCategory, Is.EqualTo(RagConfidenceCategory.Insufficient));
            Assert.That(result.Sources, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ConfidenceCategory_MapsScoreBandsAndTreatsNonAnsweredAsInsufficient()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RagConfidenceCategory.From(RagStatus.Answered, 0.90), Is.EqualTo(RagConfidenceCategory.High));
            Assert.That(RagConfidenceCategory.From(RagStatus.Answered, 0.70), Is.EqualTo(RagConfidenceCategory.Medium));
            Assert.That(RagConfidenceCategory.From(RagStatus.Answered, 0.58), Is.EqualTo(RagConfidenceCategory.Low));
            // A high score with a non-answered status must never present as confident.
            Assert.That(RagConfidenceCategory.From(RagStatus.InsufficientContext, 0.95), Is.EqualTo(RagConfidenceCategory.Insufficient));
            Assert.That(RagConfidenceCategory.From(RagStatus.GenerationUnavailable, 0.95), Is.EqualTo(RagConfidenceCategory.Insufficient));
        });
    }

    [Test]
    public async Task AskAsync_GenerationUnavailable_ExplainsTheServerHasNoKey()
    {
        var client = new FakeRagServerClient
        {
            AnswerResult = Response(RagStatus.GenerationUnavailable, null, TwoSources()),
        };
        var service = new RagKnowledgeService(client, new RagCitationParser());

        var result = await service.AskAsync(Feature, "How long can I hire equipment for?", Corpus, CancellationToken.None);

        Assert.That(result.Reply, Does.Contain("Answer generation is not configured on the server"));
    }

    [Test]
    public async Task AskAsync_Answered_FallsBackToDocumentIdWhenNoLabelMetadata()
    {
        IReadOnlyList<RagSource> unlabelled = [new(1, "policy-equipment-hire", 0, 0.8, "Hire lasts seven days.", null)];
        var client = new FakeRagServerClient
        {
            AnswerResult = Response(RagStatus.Answered, "Answer [1].", unlabelled),
        };
        var service = new RagKnowledgeService(client, new RagCitationParser());

        var result = await service.AskAsync(Feature, "How long?", Corpus, CancellationToken.None);

        Assert.That(result.Sources[0].Label, Is.EqualTo("policy-equipment-hire"));
    }

    [Test]
    public void AskAsync_ThrowsWhenTheServerReturnsNothing()
    {
        var client = new FakeRagServerClient { AnswerResult = null };
        var service = new RagKnowledgeService(client, new RagCitationParser());

        Assert.That(
            async () => await service.AskAsync(Feature, "How long?", Corpus, CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>());
    }
}

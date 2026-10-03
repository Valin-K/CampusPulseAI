using CampusPulse.Shared.Ai;
using RagServer.Models;
using RagServer.Services;

namespace RagServer.Tests;

public sealed class RagRetrievalServiceTests
{
    [Test]
    public async Task RetrieveReturnsRankedTopKSourcesAndCosineSimilarity()
    {
        var store = new VectorStore();
        store.Upsert("coursemanagement", "relevant", [
            new StoredChunk("relevant", 0, "matching fixture", null, [1, 0])
        ]);
        store.Upsert("coursemanagement", "distractor", [
            new StoredChunk("distractor", 0, "unrelated fixture", null, [0, 1])
        ]);
        var gateway = new StubGateway();
        var ai = new RagAiClient(gateway);
        var service = new RagRetrievalService(store, ai);

        var result = await service.RetrieveAsync(
            "coursemanagement", "matching question", 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TopK, Is.EqualTo(1));
            Assert.That(result.Sources, Has.Count.EqualTo(1));
            Assert.That(result.Sources[0].DocumentId, Is.EqualTo("relevant"));
            Assert.That(result.Sources[0].Rank, Is.EqualTo(1));
            Assert.That(result.CosineSimilarity, Is.EqualTo(1));
            Assert.That(result.Embedder, Is.EqualTo("embedding"));
        });
    }

    private sealed class StubGateway : IAiGatewayClient
    {
        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiGatewayStatus(true, "generation", "embedding", 2));

        public Task<AiEmbeddingResponse> EmbedAsync(
            IReadOnlyList<string> texts,
            bool isQuery,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiEmbeddingResponse(
                texts.Select(_ => new float[] { 1, 0 }).ToArray(), "embedding"));

        public Task<AiGenerationResponse> GenerateAsync(
            string prompt,
            string context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiGenerationResponse("answer", "generation"));
    }
}
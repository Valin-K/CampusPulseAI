using RagServer.Models;

namespace RagServer.Services;

public sealed class RagRetrievalService(VectorStore store, RagAiClient ai)
{
    public async Task<RetrievalResponse> RetrieveAsync(
        string collection,
        string question,
        int topK,
        CancellationToken cancellationToken)
    {
        var boundedTopK = Math.Clamp(topK, 1, 20);
        var queryVector = (await ai.EmbedAsync(
            [question], isQuery: true, cancellationToken))[0];
        var sources = store.Search(collection, queryVector, boundedTopK)
            .Select((hit, index) => new RagSource(
                index + 1,
                hit.Chunk.DocumentId,
                hit.Chunk.ChunkIndex,
                Math.Round(hit.Score, 4),
                hit.Chunk.Text,
                hit.Chunk.Metadata))
            .ToList();
        var similarity = sources.Count == 0 ? 0 : sources[0].Score;

        return new RetrievalResponse(
            collection,
            question,
            boundedTopK,
            similarity,
            sources,
            ai.EmbedderName,
            DateTimeOffset.UtcNow);
    }
}
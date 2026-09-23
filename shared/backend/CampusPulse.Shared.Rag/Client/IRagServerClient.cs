using CampusPulse.Shared.Rag.Contracts;

namespace CampusPulse.Shared.Rag.Client;

public interface IRagServerClient
{
    Task<RagIngestResponse?> IngestAsync(string collection, RagIngestRequest request, CancellationToken cancellationToken);

    Task<RagAnswerResponse?> AnswerAsync(string collection, RagAnswerRequest request, CancellationToken cancellationToken);
}

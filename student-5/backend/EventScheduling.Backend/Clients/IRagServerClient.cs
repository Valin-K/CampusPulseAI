using CampusPulse.Shared.Rag.Contracts;

namespace EventScheduling.Backend.Clients;

public interface IRagServerClient
{
    Task<RagIngestResponse?> IngestAsync(string collection, RagIngestRequest request, CancellationToken cancellationToken);

    Task<RagAnswerResponse?> AnswerAsync(string collection, RagAnswerRequest request, CancellationToken cancellationToken);
}

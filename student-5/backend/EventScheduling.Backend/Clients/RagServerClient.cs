using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.Rag.Contracts;

namespace EventScheduling.Backend.Clients;

public sealed class RagServerClient(HttpClient http) : IRagServerClient
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<RagIngestResponse?> IngestAsync(
        string collection, RagIngestRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            $"/api/rag/{collection}/documents", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RagIngestResponse>(Json, cancellationToken);
    }

    public async Task<RagAnswerResponse?> AnswerAsync(
        string collection, RagAnswerRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            $"/api/rag/{collection}/answer", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RagAnswerResponse>(Json, cancellationToken);
    }
}

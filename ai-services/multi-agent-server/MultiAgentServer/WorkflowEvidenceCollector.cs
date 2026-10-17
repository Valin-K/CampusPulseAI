using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.MultiAgent;

namespace MultiAgentServer;

public interface IWorkflowEvidenceCollector
{
    Task<Workflow> CollectAsync(Workflow workflow, CancellationToken ct, string? authorization = null);
}

public sealed class WorkflowEvidenceCollector(
    IHttpClientFactory clients, ILogger<WorkflowEvidenceCollector> logger) : IWorkflowEvidenceCollector
{
    public async Task<Workflow> CollectAsync(Workflow workflow, CancellationToken ct, string? authorization = null)
    {
        var evidence = workflow.Evidence.Where(item => !item.Id.StartsWith("mcp-", StringComparison.Ordinal)
            && !item.Id.StartsWith("rag-", StringComparison.Ordinal)).ToList();
        var dependencies = new List<DependencyResult>();
        if (workflow.UseMcp)
        {
            await CollectDependencyAsync("mcp", async () =>
            {
                using var client = clients.CreateClient("workflowMcp");
                if (!string.IsNullOrWhiteSpace(authorization))
                    client.DefaultRequestHeaders.Authorization = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(authorization);
                var mcp = new McpClient(client);
                var tool = workflow.Feature switch
                {
                    "academicrecords" or "coursemanagement" => "course.list_courses",
                    "campushousing" => "room_status",
                    _ => "get_feature_capabilities"
                };
                var arguments = new Dictionary<string, JsonElement>();
                if (tool == "get_feature_capabilities")
                    arguments["feature"] = JsonSerializer.SerializeToElement(
                        workflow.Feature == "financialaidbilling" ? "student-4" : "student-5");
                var result = await mcp.CallAsync(tool, arguments, ct);
                if (result.IsError || result.Result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    throw new InvalidDataException("The MCP tool returned an error or empty result.");
                var content = result.Result.GetRawText();
                if (content.Length > 100000) throw new InvalidDataException("MCP evidence exceeds the workflow size limit.");
                evidence.Add(new("mcp-tool", $"MCP {tool}", content, result.ExecutedAtUtc));
                return "Read-only tool evidence collected; capability tools describe integration, not domain facts.";
            }, workflow.AllowFallback, dependencies, ct);
        }
        else dependencies.Add(new("mcp", "disabled", "MCP was not requested; using application/supplied evidence."));

        if (workflow.UseRag)
        {
            await CollectDependencyAsync("rag", async () =>
            {
                using var response = await clients.CreateClient("workflowRag").PostAsJsonAsync(
                    $"api/rag/{workflow.Feature}/retrieve", new { question = workflow.Task, topK = 4 }, ct);
                response.EnsureSuccessStatusCode();
                var retrieval = await response.Content.ReadFromJsonAsync<RetrievedEvidence>(ct)
                    ?? throw new InvalidDataException("RAG returned an empty retrieval.");
                if (retrieval.Sources is null || retrieval.Collection != workflow.Feature
                    || retrieval.Sources.Any(source => source is null || string.IsNullOrWhiteSpace(source.DocumentId)
                        || source.ChunkIndex < 0 || string.IsNullOrWhiteSpace(source.Text)))
                    throw new InvalidDataException("RAG returned an invalid retrieval.");
                var sources = retrieval.Sources.Where(source => double.IsFinite(source.Score)
                    && source.Score >= 0.70 && !string.IsNullOrWhiteSpace(source.Text)).ToList();
                if (sources.Count == 0) throw new InvalidDataException("RAG has no relevant indexed sources meeting the 0.70 threshold.");
                if (sources.Sum(item => (long)item.Text.Length) > 100000)
                    throw new InvalidDataException("RAG evidence exceeds the workflow size limit.");
                foreach (var source in sources)
                    evidence.Add(new($"rag-{evidence.Count}", $"RAG {source.DocumentId} chunk {source.ChunkIndex}; score {source.Score:F4}",
                        source.Text, DateTimeOffset.UtcNow));
                return $"{sources.Count} grounded source passages retrieved.";
            }, workflow.AllowFallback, dependencies, ct);
        }
        else dependencies.Add(new("rag", "disabled", "RAG was not requested; using application/supplied evidence."));
        return workflow with { Evidence = evidence, Dependencies = dependencies };
    }

    private async Task CollectDependencyAsync(
        string service, Func<Task<string>> action, bool allowFallback, List<DependencyResult> dependencies, CancellationToken ct)
    {
        try
        {
            dependencies.Add(new(service, "available", await action()));
        }
        catch (Exception exception) when (exception is HttpRequestException or McpResponseException or JsonException or InvalidDataException
            || exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Optional workflow dependency {Service} failed. Fallback allowed: {Allowed}.", service, allowFallback);
            if (!allowFallback) throw new InvalidDataException($"{service.ToUpperInvariant()} evidence is required but unavailable.", exception);
            dependencies.Add(new(service, "fallback", $"{service.ToUpperInvariant()} unavailable: {exception.Message} Using application/supplied evidence only."));
        }
    }

    private sealed record RetrievedEvidence(string Collection, IReadOnlyList<RetrievedSource> Sources);
    private sealed record RetrievedSource(string DocumentId, int ChunkIndex, double Score, string Text);
}

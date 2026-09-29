namespace CampusPulse.Shared.Ai;

public sealed record AiGenerationRequest(string Prompt, string Context);
public sealed record AiGenerationResponse(string Text, string Model);
public sealed record AiEmbeddingRequest(IReadOnlyList<string> Texts, bool IsQuery);
public sealed record AiEmbeddingResponse(float[][] Embeddings, string Model);
public sealed record AiGatewayStatus(
    bool GenerationAvailable, string GenerationModel, string EmbeddingModel, int EmbeddingDimensions);

public interface IAiGatewayClient
{
    Task<AiGenerationResponse> GenerateAsync(
        string prompt, string context, CancellationToken cancellationToken = default);

    Task<AiEmbeddingResponse> EmbedAsync(
        IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken = default);

    Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

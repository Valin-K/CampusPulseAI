namespace CampusPulse.Shared.Ai.Gateway;

public sealed class GeminiOptions
{
    public string ApiKey { get; init; } = "";
    public string GenerationModel { get; init; } = "gemini-3.5-flash-lite";
    public string EmbeddingModel { get; init; } = "gemini-embedding-001";
    public int EmbeddingDimensions { get; init; } = 768;
}

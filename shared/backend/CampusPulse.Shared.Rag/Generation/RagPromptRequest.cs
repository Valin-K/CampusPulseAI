using CampusPulse.Shared.Rag.Contracts;

namespace CampusPulse.Shared.Rag.Generation;

public sealed record RagPromptRequest
{
    public required string Question { get; init; }
    public required IReadOnlyList<RagSource> Sources { get; init; }
    public string? Persona { get; init; }
    public RagOutputFormat Format { get; init; } = RagOutputFormat.Prose;
    public string? JsonSchemaHint { get; init; }
}

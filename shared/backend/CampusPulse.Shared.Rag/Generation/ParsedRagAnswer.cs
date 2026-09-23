using CampusPulse.Shared.Rag.Contracts;

namespace CampusPulse.Shared.Rag.Generation;

public abstract record AnswerSegment;

public sealed record TextSegment(string Text) : AnswerSegment;

public sealed record CitationSegment(
    IReadOnlyList<int> DisplayNumbers,
    IReadOnlyList<CitedSource> Sources) : AnswerSegment;

public sealed record CitedSource(int DisplayNumber, RagSource Source);

public sealed record ParsedRagAnswer(
    IReadOnlyList<AnswerSegment> Segments,
    IReadOnlyList<CitedSource> Cited,
    IReadOnlyList<RagSource> Uncited,
    IReadOnlyList<int> InvalidMarkers);

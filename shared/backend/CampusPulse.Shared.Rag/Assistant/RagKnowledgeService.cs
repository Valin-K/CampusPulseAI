using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;

namespace CampusPulse.Shared.Rag.Assistant;

public sealed class RagKnowledgeService(IRagServerClient rag, RagCitationParser citationParser)
{
    public async Task<RagAssistantAnswer> AskAsync(
        RagFeature feature,
        string question,
        IReadOnlyList<RagDocument> corpus,
        CancellationToken cancellationToken,
        string? supplementalContext = null)
    {
        var trimmedQuestion = question.Trim();

        await rag.IngestAsync(feature.Collection, new RagIngestRequest(corpus), cancellationToken);

        var response = await rag.AnswerAsync(
            feature.Collection,
            new RagAnswerRequest(
                trimmedQuestion,
                TopK: 4,
                Instructions: feature.Persona,
                SupplementalContext: supplementalContext),
            cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException("The RAG server returned an empty response.");
        }

        var parsed = citationParser.Parse(response.Answer, response.Sources);
        return Shape(trimmedQuestion, response, parsed);
    }

    private static RagAssistantAnswer Shape(
        string question, RagAnswerResponse response, ParsedRagAnswer parsed)
    {
        var reply = response.Status switch
        {
            RagStatus.Answered => CitationRenderer.ToText(parsed),
            RagStatus.InsufficientContext => response.Answer
                ?? "I do not have enough information in the knowledge base to answer that.",
            RagStatus.GenerationUnavailable =>
                "Answer generation is not configured on the server, but I retrieved the relevant passages below.",
            _ => "I could not generate an answer just now. The passages below are what I retrieved.",
        };

        var sources = response.Status == RagStatus.Answered
            ? parsed.Cited.Select(cited => new RagKnowledgeSource(
                cited.DisplayNumber, Label(cited.Source), cited.Source.DocumentId, cited.Source.Score))
            : response.Sources.Select(source => new RagKnowledgeSource(
                source.Rank, Label(source), source.DocumentId, source.Score));

        return new RagAssistantAnswer(
            question,
            response.Status,
            reply,
            response.Confidence,
            RagConfidenceCategory.From(response.Status, response.Confidence),
            sources.ToList(),
            response.Embedder);
    }

    private static string Label(RagSource source)
    {
        var label = source.Metadata?
            .FirstOrDefault(pair =>
                pair.Key.Contains("title", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains("name", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains("code", StringComparison.OrdinalIgnoreCase))
            .Value;

        return string.IsNullOrWhiteSpace(label) ? source.DocumentId : label;
    }
}

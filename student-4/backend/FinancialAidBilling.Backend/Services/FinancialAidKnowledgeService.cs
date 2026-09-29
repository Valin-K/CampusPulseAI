using System.Net.Http.Json;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public sealed class FinancialAidKnowledgeService(
    IHttpClientFactory clients,
    RagKnowledgeService knowledge,
    IRagServerClient rag) : IFinancialAidKnowledgeService
{
    private static readonly RagFeature Feature = RagFeature.FinancialAidBilling;

    public async Task<RagAnswerResponse> AskScholarshipsAsync(
        RagQuestionRequest request, CancellationToken cancellationToken)
    {
        var corpus = await BuildCorpusAsync(cancellationToken);
        var ingest = await rag.IngestAsync(Feature.Collection, new RagIngestRequest(corpus), cancellationToken);
        if (ingest is null || ingest.Collection != Feature.Collection
            || ingest.Documents != corpus.Count || ingest.Chunks < 1)
        {
            throw new InvalidOperationException("The RAG server did not confirm ingestion of the scholarship corpus.");
        }

        var answer = await rag.AnswerAsync(Feature.Collection,
            new RagAnswerRequest(request.Question.Trim(), 4,
                Feature.Persona + " Cite your scholarship sources. Do not confirm personal GPA eligibility."),
            cancellationToken);
        if (answer is null || answer.Collection != Feature.Collection || answer.Sources is null
            || answer.Status is not (RagStatus.Answered or RagStatus.InsufficientContext
                or RagStatus.GenerationUnavailable or RagStatus.GenerationFailed)
            || (answer.Status == RagStatus.Answered && string.IsNullOrWhiteSpace(answer.Answer)))
        {
            throw new InvalidOperationException("The RAG server returned an invalid scholarship answer response.");
        }

        return answer;
    }

    public async Task<RagAssistantAnswer> AskAsync(
        RagQuestionRequest request, CancellationToken cancellationToken)
    {
        var corpus = await BuildCorpusAsync(cancellationToken);

        // AGENT-MIGRATION: handing the corpus to the shared RAG assistant (ask + citation parsing)
        // moves into the multi-agent server; building this feature's corpus stays here because this
        // feature owns its data.
        return await knowledge.AskAsync(Feature, request.Question, corpus, cancellationToken);
    }

    private async Task<IReadOnlyList<RagDocument>> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        var documents = new List<RagDocument>(PolicyDocuments);

        var scholarships = await clients.CreateClient("billingDatabase")
            .GetFromJsonAsync<List<ScholarshipResponse>>("/api/scholarships", cancellationToken)
            ?? throw new InvalidOperationException("The scholarship database returned an empty response.");
        documents.AddRange(scholarships.Select(ScholarshipDocument));

        return documents;
    }

    private static RagDocument ScholarshipDocument(ScholarshipResponse scholarship) =>
        new(
            $"scholarship-{scholarship.Id}",
            FormattableString.Invariant($"{scholarship.Name}. {scholarship.Description} Award amount: ${scholarship.AwardAmount:F2}. ")
                + FormattableString.Invariant($"Minimum GPA (4-point scale): {scholarship.MinimumGpa:F2}. Eligible major: {scholarship.EligibleMajor}. ")
                + $"Minimum year level: {scholarship.MinimumYearLevel}. Application deadline: {scholarship.ApplicationDeadline:MMMM d, yyyy}. "
                + $"Applications are {(scholarship.IsActive ? "open" : "closed")}.",
            new Dictionary<string, string> { ["title"] = scholarship.Name, ["scholarshipName"] = scholarship.Name });

    private static readonly IReadOnlyList<RagDocument> PolicyDocuments =
    [
        new(
            "policy-scholarships",
            "Scholarships. Each scholarship lists an award amount, a minimum GPA, an eligible major, a minimum "
            + "year level and an application deadline. A student can apply for a scholarship while its applications "
            + "are open, the deadline has not passed and they meet its eligibility requirements. "
            + "Scholarship minimum GPA uses a 4-point scale. Academic Records uses a 7-point GPA scale; "
            + "no conversion policy is configured, so personal GPA eligibility requires aid-office review.",
            new Dictionary<string, string> { ["title"] = "Scholarships" }),
    ];
}

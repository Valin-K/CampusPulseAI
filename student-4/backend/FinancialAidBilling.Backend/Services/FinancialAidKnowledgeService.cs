using System.Net.Http.Json;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public sealed class FinancialAidKnowledgeService(
    IHttpClientFactory clients,
    RagKnowledgeService knowledge,
    ILogger<FinancialAidKnowledgeService> logger) : IFinancialAidKnowledgeService
{
    private static readonly RagFeature Feature = RagFeature.FinancialAidBilling;

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

        try
        {
            var scholarships = await clients.CreateClient("billingDatabase")
                .GetFromJsonAsync<List<ScholarshipResponse>>("/api/scholarships", cancellationToken);
            documents.AddRange(scholarships?.Select(ScholarshipDocument) ?? []);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not load scholarships for the knowledge corpus.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading scholarships for the knowledge corpus.");
        }

        return documents;
    }

    private static RagDocument ScholarshipDocument(ScholarshipResponse scholarship) =>
        new(
            $"scholarship-{scholarship.Id}",
            $"{scholarship.Name}. {scholarship.Description} Award amount: {scholarship.AwardAmount:C0}. "
                + $"Minimum GPA: {scholarship.MinimumGpa:F2}. Eligible major: {scholarship.EligibleMajor}. "
                + $"Minimum year level: {scholarship.MinimumYearLevel}. Application deadline: {scholarship.ApplicationDeadline:MMMM d, yyyy}. "
                + $"Applications are {(scholarship.IsActive ? "open" : "closed")}.",
            new Dictionary<string, string> { ["title"] = scholarship.Name });

    private static readonly IReadOnlyList<RagDocument> PolicyDocuments =
    [
        new(
            "policy-scholarships",
            "Scholarships. Each scholarship lists an award amount, a minimum GPA, an eligible major, a minimum "
            + "year level and an application deadline. A student can apply for a scholarship while its applications "
            + "are open and they meet its eligibility requirements.",
            new Dictionary<string, string> { ["title"] = "Scholarships" }),
    ];
}

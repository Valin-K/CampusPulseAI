using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public interface IFinancialAidKnowledgeService
{
    Task<RagAssistantAnswer> AskAsync(RagQuestionRequest request, CancellationToken cancellationToken);
    Task<RagAnswerResponse> AskScholarshipsAsync(RagQuestionRequest request, CancellationToken cancellationToken);
}

using CampusPulse.Shared.Rag.Assistant;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public interface IFinancialAidKnowledgeService
{
    Task<RagAssistantAnswer> AskAsync(RagQuestionRequest request, CancellationToken cancellationToken);
}

using CampusHousing.Backend.Models;
using CampusPulse.Shared.Rag.Assistant;

namespace CampusHousing.Backend.Services;

public interface IHousingKnowledgeService
{
    Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

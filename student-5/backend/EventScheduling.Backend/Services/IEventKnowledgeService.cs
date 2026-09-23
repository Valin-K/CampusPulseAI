using CampusPulse.Shared.Rag.Assistant;
using EventScheduling.Backend.Models;

namespace EventScheduling.Backend.Services;

public interface IEventKnowledgeService
{
    Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

using EventScheduling.Backend.Models;

namespace EventScheduling.Backend.Services;

public interface IEventKnowledgeService
{
    Task<KnowledgeAnswerResponse> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

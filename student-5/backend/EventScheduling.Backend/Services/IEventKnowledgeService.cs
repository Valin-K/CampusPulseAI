using CampusPulse.Shared.Rag.Contracts;
using EventScheduling.Backend.Models;

namespace EventScheduling.Backend.Services;

public interface IEventKnowledgeService
{
    Task<RagAnswerResponse> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

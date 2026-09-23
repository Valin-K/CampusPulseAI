using CampusPulse.Shared.Rag.Assistant;
using CourseManagement.Backend.Contracts.Ai;

namespace CourseManagement.Backend.Services;

public interface ICourseKnowledgeService
{
    Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

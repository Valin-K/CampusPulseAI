using AcademicRecords.Backend.Models;
using CampusPulse.Shared.Rag.Assistant;

namespace AcademicRecords.Backend.Services;

public interface IAcademicKnowledgeService
{
    Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

public interface IAcademicRagService
{
    Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

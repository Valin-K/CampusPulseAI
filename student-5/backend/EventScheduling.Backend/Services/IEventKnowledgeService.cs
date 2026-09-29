using CampusPulse.Shared.Rag.Assistant;
using EventScheduling.Backend.Models;

namespace EventScheduling.Backend.Services;

// Entry point the KnowledgeController (POST /api/ai/knowledge, the shared chat) calls. It is the
// orchestrator that routes a question across RAG, the event database and the shared MCP server.
public interface IEventKnowledgeService
{
    Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

// The RAG-only leg the orchestrator delegates to for policy and facility knowledge.
public interface IEventRagService
{
    Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken);
}

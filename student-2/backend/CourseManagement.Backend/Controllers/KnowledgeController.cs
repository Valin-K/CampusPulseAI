using CampusPulse.Shared.Rag.Assistant;
using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/ai/knowledge")]
public sealed class KnowledgeController(
    ICourseKnowledgeService knowledge,
    ILogger<KnowledgeController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RagAssistantAnswer>> Ask(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await knowledge.AskAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "The knowledge assistant could not answer.");
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The knowledge assistant could not reach the RAG server.");
            return Problem(
                "The shared RAG server is unavailable. Start it on the host at http://localhost:5267 before using this feature.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The knowledge assistant timed out.");
            return Problem(
                "The knowledge assistant did not respond in time.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}

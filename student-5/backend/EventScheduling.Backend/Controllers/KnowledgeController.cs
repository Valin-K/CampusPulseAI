using EventScheduling.Backend.Models;
using EventScheduling.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/ai/knowledge")]
public sealed class KnowledgeController(
    IEventKnowledgeService knowledge,
    ILogger<KnowledgeController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ask(KnowledgeQuestionRequest request, CancellationToken cancellationToken)
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
                "The shared RAG server is unavailable. Check that it is running and reachable at the configured Services:RagServer address.",
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

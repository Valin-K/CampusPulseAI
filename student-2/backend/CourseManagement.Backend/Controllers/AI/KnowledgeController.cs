using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Rag.Assistant;
using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace CourseManagement.Backend.Controllers.AI;

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
        catch (GeminiConfigurationException exception)
        {
            logger.LogWarning(exception, "Gemini is not configured for the knowledge assistant.");
            return Problem(
                exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (GeminiResponseException exception)
        {
            logger.LogWarning(exception, "Gemini returned an invalid knowledge-assistant response.");
            return Problem(
                "Gemini returned an invalid response.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (McpResponseException exception)
        {
            logger.LogWarning(exception, "The knowledge assistant could not use MCP.");
            return Problem(
                exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The knowledge assistant could not reach a required AI service.");
            return Problem(
                "A required AI service is unavailable.",
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

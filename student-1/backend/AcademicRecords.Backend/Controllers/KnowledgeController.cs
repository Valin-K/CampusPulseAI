using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Rag.Assistant;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[Route("api/ai/knowledge")]
public sealed class KnowledgeController(IAcademicKnowledgeService knowledge) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ask(
        [FromBody] KnowledgeQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "A question is required." });
        }

        if (request.UserId is <= 0)
        {
            return BadRequest(new { error = "User ID must be positive." });
        }

        try
        {
            return Ok(await knowledge.AskAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return Problem(
                "The shared RAG server is unavailable. Start it on the host at http://localhost:5267 before using this feature.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem("The knowledge assistant did not respond in time.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}

using EventScheduling.Backend.Models;
using EventScheduling.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/ai/assistant")]
public sealed class AssistantController(
    IBookingAssistantService assistant,
    ILogger<AssistantController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Chat(AssistantChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await assistant.ChatAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "The scheduling assistant could not answer.");
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The scheduling assistant could not reach a dependency.");
            return Problem("The scheduling assistant is currently unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The scheduling assistant timed out.");
            return Problem("The scheduling assistant did not respond in time.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

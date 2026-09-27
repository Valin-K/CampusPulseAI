using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace CourseManagement.Backend.Controllers.AI;

[ApiController]
[Route("api/ai/course-questions")]
public sealed class CourseQuestionsController(
    ICourseQuestionService courseQuestions,
    ILogger<CourseQuestionsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CourseQuestionResponse>> Ask(
        CourseQuestionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await courseQuestions.AskAsync(
                request,
                cancellationToken));
        }
        catch (GeminiConfigurationException exception)
        {
            logger.LogWarning(
                exception,
                "Gemini Course AI-Mode is not configured.");
            return AiProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Gemini is not configured",
                exception.Message);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Gemini was unavailable for a Course question.");
            return AiProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Gemini is unavailable",
                "The Course question could not be answered because Gemini is unavailable.");
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Gemini timed out while answering a Course question.");
            return AiProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Gemini is unavailable",
                "The Course question could not be answered because Gemini did not respond in time.");
        }
        catch (GeminiResponseException exception)
        {
            logger.LogWarning(
                exception,
                "Gemini returned an invalid Course question response.");
            return AiProblem(
                StatusCodes.Status502BadGateway,
                "Invalid Gemini response",
                "The Course question could not be answered because Gemini returned an invalid response.");
        }
    }

    ObjectResult AiProblem(int status, string title, string detail) =>
        StatusCode(
            status,
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail
            });
}

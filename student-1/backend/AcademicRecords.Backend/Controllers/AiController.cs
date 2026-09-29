using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Mcp.Client;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[Route("api")]
public sealed class AiController(
    IHttpClientFactory clients,
    StudentCourseMetadataService courseMetadata,
    GeminiAcademicProgressService progressService,
    IAcademicKnowledgeService knowledge) : AcademicRecordsControllerBase(clients)
{
    [HttpPost("students/{studentId}/ai-analysis")]
    public async Task<IActionResult> AnalyzeProgress(string studentId, CancellationToken cancellationToken)
    {
        var (profile, error) = await this.GetStudentProfileAsync(AcademicRecordsDatabase, studentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var courses = await courseMetadata.GetCourseMetadataAsync(cancellationToken);
        var analysis = await progressService.AnalyzeAsync(
            StudentCourseMetadataService.ApplyCourseMetadata(profile!, courses),
            cancellationToken);
        return Ok(analysis);
    }

    [HttpPost("ai/knowledge")]
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
        catch (AcademicGeminiConfigurationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AcademicGeminiResponseException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (McpResponseException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return Problem(
                "A required AI service is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem("The knowledge assistant did not respond in time.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

}

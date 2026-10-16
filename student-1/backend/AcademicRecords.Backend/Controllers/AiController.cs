using System.Net;
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

        var authenticatedUser = ControllerContext.HttpContext?.User;
        if (authenticatedUser?.IsInRole("Student") == true)
        {
            var userIdClaim = authenticatedUser.FindFirst("user_id")?.Value;
            if (!int.TryParse(userIdClaim, out var authenticatedUserId) || authenticatedUserId <= 0
                || request.UserId is not null && request.UserId != authenticatedUserId)
            {
                return Forbid();
            }

            request.UserId = authenticatedUserId;
        }
        else if (request.UserId is not null)
        {
            return Forbid();
        }

        try
        {
            return Ok(await knowledge.AskAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (McpResponseException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException exception)
        {
            var statusCode = exception.StatusCode switch
            {
                HttpStatusCode.BadGateway => StatusCodes.Status502BadGateway,
                HttpStatusCode.GatewayTimeout => StatusCodes.Status504GatewayTimeout,
                HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests =>
                    StatusCodes.Status503ServiceUnavailable,
                null => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status502BadGateway
            };
            return Problem(
                statusCode == StatusCodes.Status502BadGateway
                    ? "A required AI service returned an invalid response."
                    : "A required AI service is unavailable.",
                statusCode: statusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem("The knowledge assistant did not respond in time.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

}

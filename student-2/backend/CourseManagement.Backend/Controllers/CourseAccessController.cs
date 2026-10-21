using CourseManagement.Backend.Clients;
using CourseManagement.Backend.Configuration;
using CourseManagement.Backend.Contracts.CourseAccess;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/course-access")]
[Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
public sealed class CourseAccessController(
    IAcademicRecordsClient academicRecords,
    IOptions<CourseAccessOptions> options,
    ILogger<CourseAccessController> logger) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<CourseAccessUserResponse>>> GetUsers(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AcademicRecordsUserResponse> users;
        try
        {
            users = await academicRecords.GetUsersAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Academic Records was unavailable while resolving Course access users.");
            return UpstreamProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Academic Records unavailable",
                "Course access could not be verified because Academic Records is unavailable.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Academic Records timed out while resolving Course access users.");
            return UpstreamProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Academic Records unavailable",
                "Course access could not be verified because Academic Records did not respond in time.");
        }
        catch (AcademicRecordsResponseException exception)
        {
            logger.LogWarning(
                exception,
                "Academic Records returned an invalid Course access user response.");
            return UpstreamProblem(
                StatusCodes.Status502BadGateway,
                "Invalid Academic Records response",
                "Course access could not be verified because Academic Records returned an invalid response.");
        }

        var configuredStudentId = options.Value.DemoStudentId.Trim();
        var configuredStaffId = options.Value.DemoStaffId.Trim();
        var students = users
            .Where(user =>
                string.Equals(user.Role, "Student", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    user.StudentId,
                    configuredStudentId,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var staff = users
            .Where(user =>
                string.Equals(user.Role, "Staff", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    user.StaffId,
                    configuredStaffId,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (students.Count != 1
            || staff.Count != 1
            || students[0].UserId == staff[0].UserId)
        {
            logger.LogWarning(
                "Academic Records did not return one distinct configured Student {StudentId} " +
                "and Staff member {StaffId}.",
                configuredStudentId,
                configuredStaffId);
            return UpstreamProblem(
                StatusCodes.Status502BadGateway,
                "Required demonstration users unavailable",
                $"Academic Records must return exactly one Student '{configuredStudentId}' " +
                $"and one Staff member '{configuredStaffId}'.");
        }

        try
        {
            return Ok(new[]
            {
                ToCourseAccessUser(
                    students[0],
                    configuredStudentId,
                    CourseAccessRoles.Student),
                ToCourseAccessUser(
                    staff[0],
                    configuredStaffId,
                    CourseAccessRoles.CourseAdministrator)
            });
        }
        catch (AcademicRecordsResponseException exception)
        {
            logger.LogWarning(
                exception,
                "Academic Records returned incomplete configured Course access users.");
            return UpstreamProblem(
                StatusCodes.Status502BadGateway,
                "Invalid Academic Records response",
                "Course access could not be verified because a demonstration user was incomplete.");
        }
    }

    static CourseAccessUserResponse ToCourseAccessUser(
        AcademicRecordsUserResponse user,
        string externalId,
        string courseRole)
    {
        if (user.UserId <= 0
            || string.IsNullOrWhiteSpace(user.FirstName)
            || string.IsNullOrWhiteSpace(user.LastName)
            || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new AcademicRecordsResponseException(
                "Academic Records returned an incomplete demonstration user.");
        }

        var firstName = user.FirstName.Trim();
        var lastName = user.LastName.Trim();
        return new CourseAccessUserResponse(
            user.UserId,
            externalId,
            $"{firstName} {lastName}",
            user.Email.Trim(),
            user.Role.Trim(),
            courseRole);
    }

    ObjectResult UpstreamProblem(
        int status,
        string title,
        string detail) =>
        StatusCode(
            status,
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail
            });
}

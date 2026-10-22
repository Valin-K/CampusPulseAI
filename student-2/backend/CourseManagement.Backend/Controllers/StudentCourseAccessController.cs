using CourseManagement.Backend.Clients;
using CourseManagement.Backend.Configuration;
using CourseManagement.Backend.Contracts.CourseAccess;
using System.Security.Claims;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/course-access/student")]
[Authorize(Policy = CampusPulseSecurityExtensions.StudentOnlyPolicy)]
public sealed class StudentCourseAccessController(
    IAcademicRecordsClient academicRecords,
    ICourseCatalogueClient courseCatalogue,
    IOptions<CourseAccessOptions> options,
    ILogger<StudentCourseAccessController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StudentCourseSummaryResponse>> GetSummary(
        CancellationToken cancellationToken)
    {
        try
        {
            var studentId = User.FindFirst("student_id")?.Value;
            if (string.IsNullOrWhiteSpace(studentId)) return Forbid();
            var student = await academicRecords.GetStudentAsync(
                studentId,
                cancellationToken);
            return Ok(ToSummary(student));
        }
        catch (HttpRequestException exception)
        {
        var studentId = User.FindFirst("student_id")?.Value;
        if (string.IsNullOrWhiteSpace(studentId)) return Forbid();
            return AcademicRecordsUnavailable(exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return AcademicRecordsUnavailable(exception);
        }
        catch (AcademicRecordsResponseException exception)
        {
            return InvalidAcademicRecordsResponse(exception);
        }
    }

    [HttpPost("enrollments")]
    public async Task<ActionResult<StudentCourseSummaryResponse>> CreateEnrollment(
        CreateCourseEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var studentId = User.FindFirst("student_id")?.Value;
        if (string.IsNullOrWhiteSpace(studentId)) return Forbid();
        var courseCode = request.CourseCode?.Trim();
        if (string.IsNullOrWhiteSpace(courseCode) || courseCode.Length > 20)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Course enrolment",
                Detail = "A Course code containing no more than 20 characters is required."
            });
        }

        if (request.CompletedCourses is null
            || request.CompletedCourses.Any(completedCourse =>
                string.IsNullOrWhiteSpace(completedCourse.CourseCode)
                || completedCourse.CourseCode.Length > 20
                || string.IsNullOrWhiteSpace(completedCourse.Grade)
                || completedCourse.Grade.Length > 20))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid prerequisite evidence",
                Detail = "Completed Courses require a Course code and grade containing no more than 20 characters."
            });
        }

        CourseCatalogueResponse? course;
        try
        {
            course = await courseCatalogue.GetByCodeAsync(
                courseCode,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return CourseCatalogueUnavailable(exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CourseCatalogueUnavailable(exception);
        }
        catch (CourseCatalogueResponseException exception)
        {
            return InvalidCourseCatalogueResponse(exception);
        }

        if (course is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Course not found",
                Detail = $"Course {courseCode} does not exist in the Course catalogue."
            });
        }

        if (!course.IsActive)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Course unavailable",
                Detail = $"Course {courseCode} is not currently available for enrolment."
            });
        }

        CourseCatalogueEligibilityResponse eligibility;
        try
        {
            eligibility = await courseCatalogue.CheckPrerequisiteEligibilityAsync(
                course.Id,
                request.CompletedCourses,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return CourseCatalogueUnavailable(exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CourseCatalogueUnavailable(exception);
        }
        catch (CourseCatalogueResponseException exception)
        {
            return InvalidCourseCatalogueResponse(exception);
        }

        if (eligibility.CourseId != course.Id
            || !string.Equals(
                eligibility.CourseCode,
                course.Code,
                StringComparison.OrdinalIgnoreCase))
        {
            return InvalidCourseCatalogueResponse(
                new CourseCatalogueResponseException(
                    "Course Management Database returned prerequisite eligibility for a different Course."));
        }

        if (!eligibility.IsEligible)
        {
            var unmetRequirements = eligibility.Requirements
                .Where(requirement => !requirement.IsMet)
                .Select(requirement => requirement.Reason)
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .ToList();
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Prerequisites not met",
                Detail = unmetRequirements.Count > 0
                    ? string.Join(" ", unmetRequirements)
                    : $"Prerequisite requirements for {course.Code} are not met."
            });
        }

        try
        {
            var student = await academicRecords.GetStudentAsync(
                studentId,
                cancellationToken);
            if (student.Enrollments.Any(enrollment =>
                    string.Equals(
                        enrollment.CourseCode,
                        courseCode,
                        StringComparison.OrdinalIgnoreCase)
                    && enrollment.AcademicTermId
                        == options.Value.DemoEnrollmentTermId))
            {
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Already enrolled",
                    Detail = $"The Student already has an enrolment for {courseCode} in the configured term."
                });
            }

            await academicRecords.CreateEnrollmentAsync(
                studentId,
                new AcademicRecordsEnrollmentRequest(
                    course.Code,
                    options.Value.DemoEnrollmentTermId,
                    options.Value.DemoAcademicYear),
                cancellationToken);

            var updatedStudent = await academicRecords.GetStudentAsync(
                studentId,
                cancellationToken);
            return CreatedAtAction(
                nameof(GetSummary),
                value: ToSummary(updatedStudent));
        }
        catch (HttpRequestException exception)
        {
            return AcademicRecordsUnavailable(exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return AcademicRecordsUnavailable(exception);
        }
        catch (AcademicRecordsResponseException exception)
        {
            return InvalidAcademicRecordsResponse(exception);
        }
    }

    static StudentCourseSummaryResponse ToSummary(
        AcademicRecordsStudentResponse student)
    {
        var enrollments = student.Enrollments
            .Select(enrollment => new CourseEnrollmentResponse(
                enrollment.EnrollmentId,
                enrollment.CourseCode,
                enrollment.Status,
                enrollment.Credits,
                enrollment.IsFinalized,
                IsCurrent(enrollment)))
            .OrderBy(enrollment => enrollment.CourseCode)
            .ToList();
        var enrolledCourseCount = enrollments
            .Where(enrollment => enrollment.IsCurrent)
            .Select(enrollment => enrollment.CourseCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var completedCredits = enrollments
            .Where(enrollment =>
                enrollment.IsFinalized
                && (string.Equals(
                        enrollment.Status,
                        "Passed",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        enrollment.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase)))
            .Sum(enrollment => enrollment.Credits ?? 0);

        return new StudentCourseSummaryResponse(
            enrolledCourseCount,
            completedCredits,
            enrollments);
    }

    static bool IsCurrent(AcademicRecordsEnrollmentResponse enrollment) =>
        !enrollment.IsFinalized
        && (string.Equals(
                enrollment.Status,
                "Enrolled",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                enrollment.Status,
                "In Progress",
                StringComparison.OrdinalIgnoreCase));

    ObjectResult AcademicRecordsUnavailable(Exception exception)
    {
        logger.LogWarning(
            exception,
            "Academic Records was unavailable while processing Course enrolments.");
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Academic Records unavailable",
                Detail = "Course enrolment information is unavailable because Academic Records could not be reached."
            });
    }

    ObjectResult CourseCatalogueUnavailable(Exception exception)
    {
        logger.LogWarning(
            exception,
            "Course Management Database was unavailable while validating an enrolment.");
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Course catalogue unavailable",
                Detail = "The enrolment could not be completed because the Course catalogue could not be reached."
            });
    }

    ObjectResult InvalidCourseCatalogueResponse(
        CourseCatalogueResponseException exception)
    {
        logger.LogWarning(
            exception,
            "Course Management Database returned an invalid Course response.");
        return StatusCode(
            StatusCodes.Status502BadGateway,
            new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Invalid Course catalogue response",
                Detail = "The enrolment could not be completed because the Course catalogue returned an invalid response."
            });
    }

    ObjectResult InvalidAcademicRecordsResponse(
        AcademicRecordsResponseException exception)
    {
        logger.LogWarning(
            exception,
            "Academic Records returned an invalid Course enrolment response.");
        return StatusCode(
            StatusCodes.Status502BadGateway,
            new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Invalid Academic Records response",
                Detail = "Course enrolment could not be completed because Academic Records returned an invalid response."
            });
    }
}

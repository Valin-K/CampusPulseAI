using CourseManagement.Backend.BusinessRules;
using CourseManagement.Backend.Contracts.Courses;
using CourseManagement.Backend.Contracts.FacultyAssignments;
using CourseManagement.Backend.Contracts.Prerequisites;
using CourseManagement.Backend.Data;
using CourseManagement.Backend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/courses")]
public sealed class CoursesController(CourseManagementDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CourseResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var courses = await database.Courses
            .AsNoTracking()
            .OrderBy(course => course.Code)
            .Select(course => new CourseResponse(
                course.Id,
                course.Code,
                course.Name,
                course.Description,
                course.CreditPoints,
                course.Level,
                course.IsActive,
                course.SyllabusFileName))
            .ToListAsync(cancellationToken);

        return Ok(courses);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CourseDetailResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var course = await database.Courses
            .AsNoTracking()
            .Include(item => item.Prerequisites)
                .ThenInclude(prerequisite => prerequisite.RequiredCourse)
            .Include(item => item.FacultyAssignments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return course is null ? NotFoundProblem(id) : Ok(ToDetailResponse(course));
    }

    [HttpPost]
    public async Task<ActionResult<CourseResponse>> Create(
        CourseRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.Code);
        if (await database.Courses.AnyAsync(course => course.Code == code, cancellationToken))
        {
            return ConflictProblem($"A course with code '{code}' already exists.");
        }

        var course = new Course
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            CreditPoints = request.CreditPoints,
            Level = request.Level,
            IsActive = request.IsActive
        };

        database.Courses.Add(course);
        await database.SaveChangesAsync(cancellationToken);

        var response = ToResponse(course);
        return CreatedAtAction(nameof(GetById), new { id = course.Id }, response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CourseResponse>> Update(
        int id,
        CourseRequest request,
        CancellationToken cancellationToken)
    {
        var course = await database.Courses.FindAsync([id], cancellationToken);
        if (course is null)
        {
            return NotFoundProblem(id);
        }

        var code = NormalizeCode(request.Code);
        if (await database.Courses.AnyAsync(
                item => item.Id != id && item.Code == code,
                cancellationToken))
        {
            return ConflictProblem($"A course with code '{code}' already exists.");
        }

        course.Code = code;
        course.Name = request.Name.Trim();
        course.Description = request.Description.Trim();
        course.CreditPoints = request.CreditPoints;
        course.Level = request.Level;
        course.IsActive = request.IsActive;

        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(course));
    }

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<CourseResponse>> SetStatus(
        int id,
        CourseStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.IsActive is null)
        {
            ModelState.AddModelError(nameof(request.IsActive), "Course status is required.");
            return BadRequest(new ValidationProblemDetails(ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            });
        }

        var course = await database.Courses.FindAsync([id], cancellationToken);
        if (course is null)
        {
            return NotFoundProblem(id);
        }

        course.IsActive = request.IsActive.Value;
        await database.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(course));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var course = await database.Courses.FindAsync([id], cancellationToken);
        if (course is null)
        {
            return NotFoundProblem(id);
        }

        var dependentCourseCodes = await database.Prerequisites
            .AsNoTracking()
            .Where(prerequisite => prerequisite.RequiredCourseId == id)
            .Select(prerequisite => prerequisite.Course.Code)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);

        if (dependentCourseCodes.Count > 0)
        {
            return ConflictProblem(
                $"Course '{course.Code}' cannot be deleted because it is required by: " +
                string.Join(", ", dependentCourseCodes));
        }

        database.Courses.Remove(course);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:int}/prerequisite-eligibility")]
    public async Task<ActionResult<PrerequisiteEligibilityResponse>> CheckPrerequisiteEligibility(
        int id,
        PrerequisiteEligibilityRequest request,
        CancellationToken cancellationToken)
    {
        var course = await database.Courses
            .AsNoTracking()
            .Include(item => item.Prerequisites)
                .ThenInclude(prerequisite => prerequisite.RequiredCourse)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (course is null)
        {
            return NotFoundProblem(id);
        }

        var duplicateCodes = request.CompletedCourses
            .GroupBy(item => NormalizeCode(item.CourseCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateCodes.Count > 0)
        {
            return BadRequestProblem(
                $"Completed courses contain duplicate codes: {string.Join(", ", duplicateCodes)}.");
        }

        var completedCourses = new Dictionary<string, (string Grade, int Rank)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var completedCourse in request.CompletedCourses)
        {
            if (!GradeScale.TryGetRank(completedCourse.Grade, out var rank))
            {
                return BadRequestProblem(
                    $"Grade '{completedCourse.Grade}' is not supported. " +
                    "Use Fail, Pass, Credit, Distinction, High Distinction, or their abbreviations.");
            }

            completedCourses.Add(
                NormalizeCode(completedCourse.CourseCode),
                (completedCourse.Grade.Trim(), rank));
        }

        var requirements = new List<PrerequisiteRequirementResponse>();
        foreach (var prerequisite in course.Prerequisites.OrderBy(item => item.RequiredCourse.Code))
        {
            var minimumGrade = string.IsNullOrWhiteSpace(prerequisite.MinimumGrade)
                ? "Pass"
                : prerequisite.MinimumGrade;

            if (!GradeScale.TryGetRank(minimumGrade, out var minimumRank))
            {
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Invalid prerequisite configuration",
                    detail: $"Prerequisite {prerequisite.Id} has unsupported minimum grade '{minimumGrade}'.");
            }

            var hasCompletedCourse = completedCourses.TryGetValue(
                prerequisite.RequiredCourse.Code,
                out var completedCourse);
            var isMet = hasCompletedCourse && completedCourse.Rank >= minimumRank;

            requirements.Add(new PrerequisiteRequirementResponse(
                prerequisite.RequiredCourseId,
                prerequisite.RequiredCourse.Code,
                minimumGrade,
                hasCompletedCourse ? completedCourse.Grade : null,
                isMet));
        }

        return Ok(new PrerequisiteEligibilityResponse(
            course.Id,
            course.Code,
            requirements.All(requirement => requirement.IsMet),
            requirements));
    }

    ActionResult NotFoundProblem(int id)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Course not found",
            Detail = $"Course {id} does not exist."
        });
    }

    ActionResult ConflictProblem(string detail)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Course conflict",
            Detail = detail
        });
    }

    ActionResult BadRequestProblem(string detail)
    {
        return BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid prerequisite eligibility request",
            Detail = detail
        });
    }

    static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    static CourseResponse ToResponse(Course course)
    {
        return new CourseResponse(
            course.Id,
            course.Code,
            course.Name,
            course.Description,
            course.CreditPoints,
            course.Level,
            course.IsActive,
            course.SyllabusFileName);
    }

    static CourseDetailResponse ToDetailResponse(Course course)
    {
        var prerequisites = course.Prerequisites
            .OrderBy(prerequisite => prerequisite.RequiredCourse.Code)
            .Select(prerequisite => new PrerequisiteResponse(
                prerequisite.Id,
                prerequisite.CourseId,
                course.Code,
                prerequisite.RequiredCourseId,
                prerequisite.RequiredCourse.Code,
                prerequisite.MinimumGrade))
            .ToList();

        var assignments = course.FacultyAssignments
            .OrderBy(assignment => assignment.TeachingPeriod)
            .ThenBy(assignment => assignment.StaffName)
            .Select(assignment => new FacultyAssignmentResponse(
                assignment.Id,
                assignment.CourseId,
                course.Code,
                assignment.StaffName,
                assignment.StaffEmail,
                assignment.Role,
                assignment.TeachingPeriod))
            .ToList();

        return new CourseDetailResponse(
            course.Id,
            course.Code,
            course.Name,
            course.Description,
            course.CreditPoints,
            course.Level,
            course.IsActive,
            course.SyllabusFileName,
            prerequisites,
            assignments);
    }
}

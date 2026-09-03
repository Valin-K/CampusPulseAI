using CourseManagement.Backend.BusinessRules;
using CourseManagement.Backend.Contracts.Prerequisites;
using CourseManagement.Database.Data;
using CourseManagement.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/prerequisites")]
public sealed class PrerequisitesController(CourseManagementDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PrerequisiteResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var prerequisites = await database.Prerequisites
            .AsNoTracking()
            .Include(item => item.Course)
            .Include(item => item.RequiredCourse)
            .OrderBy(item => item.Course.Code)
            .ThenBy(item => item.RequiredCourse.Code)
            .ToListAsync(cancellationToken);

        return Ok(prerequisites.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PrerequisiteResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var prerequisite = await database.Prerequisites
            .AsNoTracking()
            .Include(item => item.Course)
            .Include(item => item.RequiredCourse)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return prerequisite is null ? NotFoundProblem(id) : Ok(ToResponse(prerequisite));
    }

    [HttpPost]
    public async Task<ActionResult<PrerequisiteResponse>> Create(
        PrerequisiteRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = await ValidateRequest(request, null, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        var prerequisite = new Prerequisite
        {
            CourseId = request.CourseId,
            RequiredCourseId = request.RequiredCourseId,
            MinimumGrade = NormalizeGrade(request.MinimumGrade)
        };

        database.Prerequisites.Add(prerequisite);
        await database.SaveChangesAsync(cancellationToken);
        await LoadRelatedCourses(prerequisite, cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = prerequisite.Id },
            ToResponse(prerequisite));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PrerequisiteResponse>> Update(
        int id,
        PrerequisiteRequest request,
        CancellationToken cancellationToken)
    {
        var prerequisite = await database.Prerequisites.FindAsync([id], cancellationToken);
        if (prerequisite is null)
        {
            return NotFoundProblem(id);
        }

        var validationError = await ValidateRequest(request, id, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        prerequisite.CourseId = request.CourseId;
        prerequisite.RequiredCourseId = request.RequiredCourseId;
        prerequisite.MinimumGrade = NormalizeGrade(request.MinimumGrade);

        await database.SaveChangesAsync(cancellationToken);
        await LoadRelatedCourses(prerequisite, cancellationToken);

        return Ok(ToResponse(prerequisite));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var prerequisite = await database.Prerequisites.FindAsync([id], cancellationToken);
        if (prerequisite is null)
        {
            return NotFoundProblem(id);
        }

        database.Prerequisites.Remove(prerequisite);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    async Task<ActionResult?> ValidateRequest(
        PrerequisiteRequest request,
        int? existingId,
        CancellationToken cancellationToken)
    {
        if (request.CourseId == request.RequiredCourseId)
        {
            return BadRequestProblem("A course cannot be its own prerequisite.");
        }

        if (!string.IsNullOrWhiteSpace(request.MinimumGrade)
            && !GradeScale.TryGetRank(request.MinimumGrade, out _))
        {
            return BadRequestProblem(
                $"Minimum grade '{request.MinimumGrade}' is not supported. " +
                "Use Fail, Pass, Credit, Distinction, High Distinction, or their abbreviations.");
        }

        var courseIds = await database.Courses
            .AsNoTracking()
            .Where(course => course.Id == request.CourseId || course.Id == request.RequiredCourseId)
            .Select(course => course.Id)
            .ToListAsync(cancellationToken);

        if (!courseIds.Contains(request.CourseId))
        {
            return NotFoundCourseProblem(request.CourseId);
        }

        if (!courseIds.Contains(request.RequiredCourseId))
        {
            return NotFoundCourseProblem(request.RequiredCourseId);
        }

        var duplicateExists = await database.Prerequisites.AnyAsync(
            item => item.Id != existingId
                && item.CourseId == request.CourseId
                && item.RequiredCourseId == request.RequiredCourseId,
            cancellationToken);

        return duplicateExists
            ? ConflictProblem("This prerequisite relationship already exists.")
            : null;
    }

    async Task LoadRelatedCourses(
        Prerequisite prerequisite,
        CancellationToken cancellationToken)
    {
        await database.Entry(prerequisite)
            .Reference(item => item.Course)
            .LoadAsync(cancellationToken);
        await database.Entry(prerequisite)
            .Reference(item => item.RequiredCourse)
            .LoadAsync(cancellationToken);
    }

    ActionResult NotFoundProblem(int id)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Prerequisite not found",
            Detail = $"Prerequisite {id} does not exist."
        });
    }

    ActionResult NotFoundCourseProblem(int id)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Course not found",
            Detail = $"Course {id} does not exist."
        });
    }

    ActionResult BadRequestProblem(string detail)
    {
        return BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid prerequisite",
            Detail = detail
        });
    }

    ActionResult ConflictProblem(string detail)
    {
        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Prerequisite conflict",
            Detail = detail
        });
    }

    static string? NormalizeGrade(string? grade)
    {
        if (string.IsNullOrWhiteSpace(grade))
        {
            return null;
        }

        return GradeScale.TryNormalize(grade, out var canonicalGrade, out _)
            ? canonicalGrade
            : grade.Trim();
    }

    static PrerequisiteResponse ToResponse(Prerequisite prerequisite)
    {
        return new PrerequisiteResponse(
            prerequisite.Id,
            prerequisite.CourseId,
            prerequisite.Course.Code,
            prerequisite.RequiredCourseId,
            prerequisite.RequiredCourse.Code,
            prerequisite.RequiredCourse.Name,
            prerequisite.MinimumGrade);
    }
}

using CourseManagement.Backend.Contracts.FacultyAssignments;
using CourseManagement.Database.Data;
using CourseManagement.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/faculty-assignments")]
public sealed class FacultyAssignmentsController(CourseManagementDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FacultyAssignmentResponse>>> GetAll(
        [FromQuery] int? courseId,
        CancellationToken cancellationToken)
    {
        var query = database.FacultyAssignments
            .AsNoTracking()
            .Include(assignment => assignment.Course)
            .AsQueryable();

        if (courseId.HasValue)
        {
            query = query.Where(assignment => assignment.CourseId == courseId.Value);
        }

        var assignments = await query
            .OrderBy(assignment => assignment.Course.Code)
            .ThenBy(assignment => assignment.TeachingPeriod)
            .ThenBy(assignment => assignment.StaffName)
            .ToListAsync(cancellationToken);

        return Ok(assignments.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FacultyAssignmentResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var assignment = await database.FacultyAssignments
            .AsNoTracking()
            .Include(item => item.Course)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return assignment is null ? NotFoundProblem(id) : Ok(ToResponse(assignment));
    }

    [HttpPost]
    public async Task<ActionResult<FacultyAssignmentResponse>> Create(
        FacultyAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.StaffEmail);
        var validationError = await ValidateRequest(
            request,
            normalizedEmail,
            null,
            cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        var assignment = new FacultyAssignment
        {
            CourseId = request.CourseId,
            StaffName = request.StaffName.Trim(),
            StaffEmail = normalizedEmail,
            Role = request.Role.Trim(),
            TeachingPeriod = request.TeachingPeriod.Trim()
        };

        database.FacultyAssignments.Add(assignment);
        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(assignment)
            .Reference(item => item.Course)
            .LoadAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = assignment.Id },
            ToResponse(assignment));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<FacultyAssignmentResponse>> Update(
        int id,
        FacultyAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var assignment = await database.FacultyAssignments.FindAsync([id], cancellationToken);
        if (assignment is null)
        {
            return NotFoundProblem(id);
        }

        var normalizedEmail = NormalizeEmail(request.StaffEmail);
        var validationError = await ValidateRequest(
            request,
            normalizedEmail,
            id,
            cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        assignment.CourseId = request.CourseId;
        assignment.StaffName = request.StaffName.Trim();
        assignment.StaffEmail = normalizedEmail;
        assignment.Role = request.Role.Trim();
        assignment.TeachingPeriod = request.TeachingPeriod.Trim();

        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(assignment)
            .Reference(item => item.Course)
            .LoadAsync(cancellationToken);

        return Ok(ToResponse(assignment));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var assignment = await database.FacultyAssignments.FindAsync([id], cancellationToken);
        if (assignment is null)
        {
            return NotFoundProblem(id);
        }

        database.FacultyAssignments.Remove(assignment);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    async Task<ActionResult?> ValidateRequest(
        FacultyAssignmentRequest request,
        string normalizedEmail,
        int? existingId,
        CancellationToken cancellationToken)
    {
        if (!await database.Courses.AnyAsync(
                course => course.Id == request.CourseId,
                cancellationToken))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Course not found",
                Detail = $"Course {request.CourseId} does not exist."
            });
        }

        var teachingPeriod = request.TeachingPeriod.Trim();
        var duplicateExists = await database.FacultyAssignments.AnyAsync(
            assignment => assignment.Id != existingId
                && assignment.CourseId == request.CourseId
                && assignment.StaffEmail == normalizedEmail
                && assignment.TeachingPeriod == teachingPeriod,
            cancellationToken);

        return duplicateExists
            ? Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Faculty assignment conflict",
                Detail = "This staff member is already assigned to the course for that teaching period."
            })
            : null;
    }

    ActionResult NotFoundProblem(int id)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Faculty assignment not found",
            Detail = $"Faculty assignment {id} does not exist."
        });
    }

    static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    static FacultyAssignmentResponse ToResponse(FacultyAssignment assignment)
    {
        return new FacultyAssignmentResponse(
            assignment.Id,
            assignment.CourseId,
            assignment.Course.Code,
            assignment.StaffName,
            assignment.StaffEmail,
            assignment.Role,
            assignment.TeachingPeriod);
    }
}

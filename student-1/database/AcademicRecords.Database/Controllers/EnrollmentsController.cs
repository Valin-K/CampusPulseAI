using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Database.Controllers;

[ApiController]
[Route("api/enrollments")]
public sealed class EnrollmentsController(AcademicRecordsDbContext database) : ControllerBase
{
    [HttpPut("{enrollmentId:int}/grade")]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public async Task<IActionResult> UpdateGrade(
        int enrollmentId,
        GradeEntryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.GradeValue is < 0 or > 100)
        {
            return BadRequest("Grade must be between 0 and 100.");
        }

        var enrollment = await database.Enrollments
            .Include(value => value.AcademicTerm)
            .SingleOrDefaultAsync(value => value.EnrollmentId == enrollmentId, cancellationToken);
        if (enrollment is null)
        {
            return NotFound("Enrollment was not found.");
        }

        if (enrollment.AcademicTerm is null)
        {
            return BadRequest("The enrollment does not have an academic session.");
        }

        var sessionDates = EnrollmentStatusRules.GetSessionDates(
            enrollment.AcademicYear,
            enrollment.AcademicTerm.StartWeekOfYear,
            enrollment.AcademicTerm.EndWeekOfYear);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today < sessionDates.StartDate)
        {
            return BadRequest("Grades cannot be assigned before the course starts.");
        }

        if (today > sessionDates.EndDate)
        {
            return BadRequest("Grades cannot be edited after the course ends.");
        }

        enrollment.GradeValue = request.GradeValue;
        enrollment.GradeDate = today;
        await database.SaveChangesAsync(cancellationToken);

        return Ok(new { enrollment.EnrollmentId, enrollment.GradeValue, enrollment.Status, enrollment.GradeDate });
    }
}

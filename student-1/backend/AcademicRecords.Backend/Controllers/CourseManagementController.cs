using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api")]
public sealed class CourseManagementController(IHttpClientFactory clients) : AcademicRecordsControllerBase(clients)
{
    [HttpGet("courses")]
    public async Task<IActionResult> GetCourses(CancellationToken cancellationToken)
    {
        var response = await CourseManagementDatabase.GetAsync("/api/courses", cancellationToken);
        return await ForwardResponseAsync(response, cancellationToken);
    }

    [HttpGet("faculty-assignments")]
    public async Task<IActionResult> GetFacultyAssignments(CancellationToken cancellationToken)
    {
        var response = await CourseManagementDatabase.GetAsync("/api/faculty-assignments", cancellationToken);
        return await ForwardResponseAsync(response, cancellationToken);
    }
}

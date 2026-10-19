using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api/enrollments")]
public sealed class EnrollmentsController(IHttpClientFactory clients) : AcademicRecordsControllerBase(clients)
{
    [HttpPut("{enrollmentId:int}/grade")]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public Task<IActionResult> UpdateGrade(int enrollmentId, CancellationToken cancellationToken) =>
        ForwardJsonRequestAsync(
            AcademicRecordsDatabase,
            HttpMethod.Put,
            $"/api/enrollments/{enrollmentId}/grade",
            cancellationToken);
}

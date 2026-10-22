using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api/staff")]
public sealed class StaffController(IHttpClientFactory clients) : AcademicRecordsControllerBase(clients)
{
    [HttpGet("by-user/{userId:int}")]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public async Task<IActionResult> GetByUser(int userId, CancellationToken cancellationToken)
    {
        var response = await AcademicRecordsDatabase.GetAsync($"/api/staff/by-user/{userId}", cancellationToken);
        return await ForwardResponseAsync(response, cancellationToken);
    }

    [HttpGet]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var response = await AcademicRecordsDatabase.GetAsync("/api/staff", cancellationToken);
        return await ForwardResponseAsync(response, cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost("enroll")]
    public Task<IActionResult> Enroll(CancellationToken cancellationToken) =>
        ForwardJsonRequestAsync(AcademicRecordsCredentialsDatabase, HttpMethod.Post, "/api/staff/enroll", cancellationToken);

    [HttpPut("{staffId}")]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public Task<IActionResult> Update(string staffId, CancellationToken cancellationToken) =>
        ForwardJsonRequestAsync(
            AcademicRecordsDatabase,
            HttpMethod.Put,
            $"/api/staff/{Uri.EscapeDataString(staffId)}",
            cancellationToken);
}

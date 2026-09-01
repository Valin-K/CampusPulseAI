using EventScheduling.Backend.Clients;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IAcademicRecordsClient academicRecords) : ControllerBase
{
    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetById(int userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<AcademicRecordsUser> users;
        try
        {
            users = await academicRecords.GetUsersAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Academic Records unavailable",
                Detail = "The user directory could not be reached."
            });
        }

        var user = users.FirstOrDefault(candidate => candidate.UserId == userId);
        return user is null ? NotFound() : Ok(user);
    }
}

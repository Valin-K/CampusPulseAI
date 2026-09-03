using EventScheduling.Backend.Clients;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IAcademicRecordsClient academicRecords) : ControllerBase
{
    private static readonly int[] SelectableUserIds = [4, 14];

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var users = await TryGetUsersAsync(cancellationToken);
        if (users is null)
        {
            return AcademicRecordsUnavailable();
        }

        var selectable = SelectableUserIds
            .Select(id => users.FirstOrDefault(candidate => candidate.UserId == id))
            .OfType<AcademicRecordsUser>()
            .ToList();

        return Ok(selectable);
    }

    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetById(int userId, CancellationToken cancellationToken)
    {
        var users = await TryGetUsersAsync(cancellationToken);
        if (users is null)
        {
            return AcademicRecordsUnavailable();
        }

        var user = users.FirstOrDefault(candidate => candidate.UserId == userId);
        return user is null ? NotFound() : Ok(user);
    }

    private async Task<IReadOnlyList<AcademicRecordsUser>?> TryGetUsersAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await academicRecords.GetUsersAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private ObjectResult AcademicRecordsUnavailable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Academic Records unavailable",
            Detail = "The user directory could not be reached."
        });
}

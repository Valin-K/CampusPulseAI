using EventScheduling.Backend.Clients;
using EventScheduling.Backend.Models;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(
    IHttpClientFactory httpClientFactory,
    IAcademicRecordsClient academicRecords) : ControllerBase
{
    private HttpClient DatabaseClient => httpClientFactory.CreateClient("eventDatabase");

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync("/api/bookings", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync($"/api/bookings/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Create(BookingRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out _) || User.IsInRole("Student") && !User.CanAccessUser(request.StudentId)) return Forbid();
        try
        {
            var users = await academicRecords.GetUsersAsync(cancellationToken);
            if (!users.Any(user => user.UserId == request.StudentId
                    && string.Equals(user.Role, User.IsInRole("Staff") ? "Staff" : "Student", StringComparison.OrdinalIgnoreCase)))
            {
                return BookingProblem(
                    "The booking could not be verified against a current student or staff record.", "RuleBreach");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Academic Records is down - let the database tier's own validation decide.
        }

        request = request with { IsStaff = User.IsInRole("Staff") };
        return await ForwardAsync(() => DatabaseClient.PostAsJsonAsync("/api/bookings", request, cancellationToken), cancellationToken);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, BookingRequest request, CancellationToken cancellationToken)
    {
        if (User.IsInRole("Student")
            && !User.CanAccessUser(request.StudentId)) return Forbid();
        return await ForwardAsync(() => DatabaseClient.PutAsJsonAsync($"/api/bookings/{id}", request, cancellationToken), cancellationToken);
    }

    [HttpPost("{id:int}/return")]
    public async Task<IActionResult> Return(int id, [FromBody] ReturnRequest? request, CancellationToken cancellationToken) =>
        await ForwardAsync(() => DatabaseClient.PostAsJsonAsync($"/api/bookings/{id}/return", request, cancellationToken), cancellationToken);

    [HttpPost("{id:int}/end")]
    public async Task<IActionResult> End(int id, CancellationToken cancellationToken) =>
        await ForwardAsync(() => DatabaseClient.PostAsync($"/api/bookings/{id}/end", content: null, cancellationToken), cancellationToken);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.DeleteAsync($"/api/bookings/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    private async Task<IActionResult> ForwardAsync(Func<Task<HttpResponseMessage>> sendAsync, CancellationToken cancellationToken)
    {
        try
        {
            var response = await sendAsync();
            return await response.ToActionResultAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return BookingProblem("The Event Scheduling service is currently unavailable.", "Outage", StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static ObjectResult BookingProblem(string message, string category, int statusCode = StatusCodes.Status400BadRequest)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { ["booking"] = [message] })
        {
            Status = statusCode
        };
        problem.Extensions["category"] = category;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}

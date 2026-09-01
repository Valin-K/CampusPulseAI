using EventScheduling.Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(IHttpClientFactory httpClientFactory) : ControllerBase
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
        var response = await DatabaseClient.PostAsJsonAsync("/api/bookings", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, BookingRequest request, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PutAsJsonAsync($"/api/bookings/{id}", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPost("{id:int}/return")]
    public async Task<IActionResult> Return(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PostAsync($"/api/bookings/{id}/return", content: null, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.DeleteAsync($"/api/bookings/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }
}

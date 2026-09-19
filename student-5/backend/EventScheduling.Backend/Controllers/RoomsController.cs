using EventScheduling.Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/rooms")]
public sealed class RoomsController(IHttpClientFactory httpClientFactory) : ControllerBase
{
    private HttpClient DatabaseClient => httpClientFactory.CreateClient("eventDatabase");

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync($"/api/rooms{Request.QueryString}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync($"/api/rooms/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Create(RoomRequest request, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PostAsJsonAsync("/api/rooms", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, RoomRequest request, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PutAsJsonAsync($"/api/rooms/{id}", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.DeleteAsync($"/api/rooms/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }
}

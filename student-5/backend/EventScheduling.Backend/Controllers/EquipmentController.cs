using EventScheduling.Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/equipment")]
public sealed class EquipmentController(IHttpClientFactory httpClientFactory) : ControllerBase
{
    private HttpClient DatabaseClient => httpClientFactory.CreateClient("eventDatabase");

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync("/api/equipment", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync($"/api/equipment/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Create(EquipmentRequest request, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PostAsJsonAsync("/api/equipment", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, EquipmentRequest request, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PutAsJsonAsync($"/api/equipment/{id}", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.DeleteAsync($"/api/equipment/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }
}

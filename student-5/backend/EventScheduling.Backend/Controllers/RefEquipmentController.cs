using EventScheduling.Backend.Models;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/refequipment")]
public sealed class RefEquipmentController(IHttpClientFactory httpClientFactory) : ControllerBase
{
    private HttpClient DatabaseClient => httpClientFactory.CreateClient("eventDatabase");

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync($"/api/refequipment{Request.QueryString}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.GetAsync($"/api/refequipment/{id}{Request.QueryString}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPost]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public async Task<IActionResult> Create(RefEquipmentRequest request, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PostAsJsonAsync("/api/refequipment", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public async Task<IActionResult> Update(int id, RefEquipmentRequest request, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.PutAsJsonAsync($"/api/refequipment/{id}", request, cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var response = await DatabaseClient.DeleteAsync($"/api/refequipment/{id}", cancellationToken);
        return await response.ToActionResultAsync(cancellationToken);
    }
}

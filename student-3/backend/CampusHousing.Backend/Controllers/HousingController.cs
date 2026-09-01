using System.Net.Http.Json;
using CampusHousing.Backend.Models;
using CampusHousing.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusHousing.Backend.Controllers;

[ApiController]
[Route("api/housing")]
public sealed class HousingController(IHttpClientFactory httpClientFactory, MaintenanceClassifier classifier) : ControllerBase
{
    private HttpClient Database => httpClientFactory.CreateClient("CampusHousingDatabase");

    [HttpGet("rooms")]
    public async Task<IActionResult> Rooms(CancellationToken cancellationToken) => await ForwardGetAsync("api/rooms", cancellationToken);

    [HttpGet("rooms/{id:int}")]
    public async Task<IActionResult> Room(int id, CancellationToken cancellationToken) => await ForwardGetAsync($"api/rooms/{id}", cancellationToken);

    [HttpPost("rooms")]
    public async Task<IActionResult> CreateRoom(CreateRoomRequest request, CancellationToken cancellationToken) =>
        await ForwardJsonAsync(HttpMethod.Post, "api/rooms", request, cancellationToken);

    [HttpPut("rooms/{id:int}")]
    public async Task<IActionResult> UpdateRoom(int id, CreateRoomRequest request, CancellationToken cancellationToken) =>
        await ForwardJsonAsync(HttpMethod.Put, $"api/rooms/{id}", request, cancellationToken);

    [HttpDelete("rooms/{id:int}")]
    public async Task<IActionResult> DeleteRoom(int id, CancellationToken cancellationToken) => await ForwardDeleteAsync($"api/rooms/{id}", cancellationToken);

    [HttpPost("allocations")]
    public async Task<IActionResult> Allocate(CreateAllocationRequest request, CancellationToken cancellationToken)
    {
        var response = await Database.PostAsJsonAsync("api/allocations", request, cancellationToken);
        return await ToActionResult(response);
    }

    [HttpGet("allocations")]
    public async Task<IActionResult> Allocations(CancellationToken cancellationToken) => await ForwardGetAsync("api/allocations", cancellationToken);

    [HttpPut("allocations/{id:int}")]
    public async Task<IActionResult> UpdateAllocation(int id, UpdateAllocationRequest request, CancellationToken cancellationToken) =>
        await ForwardJsonAsync(HttpMethod.Put, $"api/allocations/{id}", request, cancellationToken);

    [HttpDelete("allocations/{id:int}")]
    public async Task<IActionResult> DeleteAllocation(int id, CancellationToken cancellationToken) => await ForwardDeleteAsync($"api/allocations/{id}", cancellationToken);

    [HttpGet("tickets")]
    public async Task<IActionResult> Tickets(CancellationToken cancellationToken) => await ForwardGetAsync("api/tickets", cancellationToken);

    [HttpPost("tickets")]
    public async Task<IActionResult> CreateTicket(CreateMaintenanceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description)) return BadRequest(new { message = "Description is required." });
        var classification = await classifier.ClassifyAsync(request.Description, cancellationToken);
        var ticket = new MaintenanceTicket(0, request.StudentId, request.DormRoomId, request.Description, classification.Severity, classification.RepairQueue, "Open", DateTime.UtcNow, null);
        var response = await Database.PostAsJsonAsync("api/tickets", ticket, cancellationToken);
        return await ToActionResult(response);
    }

    [HttpPut("tickets/{id:int}")]
    public async Task<IActionResult> UpdateTicket(int id, UpdateTicketRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest(new { message = "Status is required." });
        var response = await Database.PutAsJsonAsync($"api/tickets/{id}", request, cancellationToken);
        return await ToActionResult(response);
    }

    [HttpDelete("tickets/{id:int}")]
    public async Task<IActionResult> DeleteTicket(int id, CancellationToken cancellationToken) => await ForwardDeleteAsync($"api/tickets/{id}", cancellationToken);

    private async Task<IActionResult> ForwardGetAsync(string path, CancellationToken cancellationToken)
    {
        var response = await Database.GetAsync(path, cancellationToken);
        return await ToActionResult(response);
    }

    private async Task<IActionResult> ForwardJsonAsync(HttpMethod method, string path, object value, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(value) };
        return await ToActionResult(await Database.SendAsync(request, cancellationToken));
    }

    private async Task<IActionResult> ForwardDeleteAsync(string path, CancellationToken cancellationToken) =>
        await ToActionResult(await Database.DeleteAsync(path, cancellationToken));

    private static async Task<IActionResult> ToActionResult(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return new ContentResult { StatusCode = (int)response.StatusCode, Content = content, ContentType = "application/json" };
    }
}

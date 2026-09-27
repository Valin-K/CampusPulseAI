using System.Net.Http.Json;
using EventScheduling.Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Backend.Controllers;

[ApiController]
[Route("api/ai/mcp")]
public sealed class McpController(
    IHttpClientFactory clients,
    ILogger<McpController> logger) : ControllerBase
{
    [HttpPost("integration")]
    public async Task<IActionResult> VerifyIntegration(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients.CreateClient("mcp").PostAsJsonAsync(
                "/api/mcp/tools/call",
                new McpToolCallRequest("validate_integration",
                    new Dictionary<string, object?> { ["feature"] = "student-5" }),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<McpIntegrationResponse>(cancellationToken);
            return result is null
                ? Problem("The shared MCP server returned an empty response.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Ok(result);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The MCP integration check could not reach the MCP server.");
            return Problem(
                "The shared MCP server is unavailable. Start it on the host at http://localhost:5268 before using this feature.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The MCP integration check timed out.");
            return Problem("The shared MCP server did not respond in time.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}

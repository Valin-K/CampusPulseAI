using System.Net.Http.Json;
using CampusPulse.Shared.Rag.Generation;

namespace CampusPulse.Shared.Rag.Client;

public sealed class McpServerClient(HttpClient http) : IMcpServerClient
{
    public async Task ValidateIntegrationAsync(RagFeature feature, CancellationToken cancellationToken)
    {
        var featureName = feature.Collection switch
        {
            "academicrecords" => "student-1",
            "coursemanagement" => "student-2",
            "campushousing" => "student-3",
            "financialaidbilling" => "student-4",
            "eventscheduling" => "student-5",
            _ => throw new InvalidOperationException($"No MCP feature mapping exists for '{feature.Collection}'.")
        };

        using var response = await http.PostAsJsonAsync(
            "/api/mcp/tools/call",
            new McpCallRequest("validate_integration", new Dictionary<string, object?>
            {
                ["feature"] = featureName
            }),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record McpCallRequest(string Tool, Dictionary<string, object?> Arguments);
}

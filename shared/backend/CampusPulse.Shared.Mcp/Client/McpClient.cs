using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.Mcp.Contracts;

namespace CampusPulse.Shared.Mcp.Client;

public sealed class McpClient(HttpClient httpClient) : IMcpClient
{
    public async Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => httpClient.GetAsync(
                "api/mcp/tools",
                cancellationToken));
        EnsureSuccess(response);

        try
        {
            return await response.Content
                    .ReadFromJsonAsync<List<McpToolDefinition>>(
                        cancellationToken)
                ?? throw new McpResponseException(
                    "The MCP server returned an empty tool catalogue.");
        }
        catch (JsonException exception)
        {
            throw new McpResponseException(
                "The MCP server returned an invalid tool catalogue.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new McpResponseException(
                "The MCP server returned an unsupported tool catalogue.",
                exception);
        }
    }

    public async Task<McpToolCallResponse> CallAsync(
        string tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => httpClient.PostAsJsonAsync(
                "api/mcp/tools/call",
                new McpToolCallRequest(tool, arguments),
                cancellationToken));
        EnsureSuccess(response);

        try
        {
            var result = await response.Content
                .ReadFromJsonAsync<McpToolCallResponse>(
                    cancellationToken);
            if (result is null)
            {
                throw new McpResponseException(
                    "The MCP server returned an empty tool result.");
            }

            if (result.IsError)
            {
                throw new McpResponseException(
                    $"MCP tool '{tool}' failed: {result.Result}");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new McpResponseException(
                $"MCP tool '{tool}' returned an invalid result.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new McpResponseException(
                $"MCP tool '{tool}' returned an unsupported result.",
                exception);
        }
    }

    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new McpResponseException(
                $"The MCP server returned HTTP {(int)response.StatusCode}.");
        }
    }

    static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException exception)
        {
            throw new McpResponseException(
                "The MCP server is unavailable.",
                exception);
        }
    }
}

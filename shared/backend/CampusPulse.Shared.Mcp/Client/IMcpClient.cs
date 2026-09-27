using System.Text.Json;
using CampusPulse.Shared.Mcp.Contracts;

namespace CampusPulse.Shared.Mcp.Client;

public interface IMcpClient
{
    Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
        CancellationToken cancellationToken);

    Task<McpToolCallResponse> CallAsync(
        string tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken);
}

public sealed class McpResponseException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

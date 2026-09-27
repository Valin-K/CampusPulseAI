using McpServer.Contracts;

namespace McpServer.Tools;

public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    string Owner { get; }
    Dictionary<string, string> InputSchema { get; } 

    Task<IResult> ExecuteAsync(
        McpCallRequest request,
        CancellationToken cancellationToken);
}

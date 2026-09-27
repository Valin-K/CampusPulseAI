using System.Text.Json;
using McpServer.Contracts;

namespace McpServer.Tools;

public abstract class McpTool(string name, string description, string owner, Dictionary<string, string> inputSchema) : IMcpTool
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string Owner { get; } = owner;
    public Dictionary<string, string> InputSchema { get; } = inputSchema;

    protected abstract Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken);

    public async Task<IResult> ExecuteAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var execution = await ExecuteCoreAsync(request, cancellationToken);
            return Results.Ok(
                new McpToolResult(
                    Name,
                    execution.IsError,
                    execution.Result,
                    DateTimeOffset.UtcNow));
        }
        catch (McpToolArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (HttpRequestException exception)
        {
            var message = $"The {Owner} backend is unavailable: {exception.Message}";
            return Results.Problem(message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            var message = $"The {Owner} backend did not respond in time.";
            return Results.Problem(message, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (JsonException)
        {
            var message = $"The {Owner} backend returned invalid data.";
            return Results.Problem(message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

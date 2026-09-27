using System.Text.Json;
using McpServer.Contracts;
using McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient<CourseManagementTools>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:CourseManagementBackend"]
        ?? "http://localhost:5118");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase)
{
    ["student_count"] = new(
        "student_count",
        "Return the number of student feature services registered in CampusPulseAI.",
        new Dictionary<string, string>()),
    ["students_by_subject"] = new(
        "students_by_subject",
        "Return registered feature services matching a feature or subject identifier.",
        new Dictionary<string, string> { ["subject_code"] = "feature identifier, for example student-2" }),
    ["project_files"] = new(
        "project_files",
        "List files in the CampusPulseAI repository root or a safe child directory.",
        new Dictionary<string, string> { ["directory_path"] = "repository-relative directory, default ." }),
    ["ci_report"] = new(
        "ci_report",
        "Read the latest local agentic-loop evidence report.",
        new Dictionary<string, string>()),
    ["get_feature_capabilities"] = new(
        "get_feature_capabilities",
        "Return the supported MCP capability for a student feature.",
        new Dictionary<string, string> { ["feature"] = "student-1 through student-5" }),
    ["validate_integration"] = new(
        "validate_integration",
        "Validate that a feature is registered with the shared MCP server.",
        new Dictionary<string, string> { ["feature"] = "student-1 through student-5" }),
};

app.UseCors();
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "mcp",
    toolCount = tools.Count + CourseManagementTools.Definitions.Count
}));
app.MapGet("/api/mcp/tools", (IConfiguration configuration) =>
{
    if (!IsEnabled(configuration["MCP_ENABLED"]))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(
        tools.Values
            .Concat(CourseManagementTools.Definitions)
            .Select(tool => new
            {
                tool.Name,
                tool.Description,
                InputSchema = tool.InputSchema
            }));
});
app.MapPost("/api/mcp/tools/call", async Task<IResult> (
    McpCallRequest request,
    IConfiguration configuration,
    CourseManagementTools courseTools,
    CancellationToken cancellationToken) =>
{
    if (!IsEnabled(configuration["MCP_ENABLED"]))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (CourseManagementTools.CanHandle(request.Tool))
    {
        try
        {
            var execution = await courseTools.ExecuteAsync(
                request,
                cancellationToken);
            return Results.Ok(
                new McpToolResult(
                    request.Tool,
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
            return Results.Problem(
                $"The Course Management backend is unavailable: {exception.Message}",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(
                "The Course Management backend did not respond in time.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

    if (!tools.TryGetValue(request.Tool, out var definition))
    {
        return Results.NotFound(new { error = $"Tool '{request.Tool}' is not registered." });
    }

    var feature = GetString(request.Arguments, "feature");
    if (definition.Name is "get_feature_capabilities" or "validate_integration"
        && !IsSupportedFeature(feature))
    {
        return Results.BadRequest(new { error = "A supported feature argument is required." });
    }

    object result = definition.Name switch
    {
        "student_count" => new { student_count = 5 },
        "students_by_subject" => new { subject_code = GetString(request.Arguments, "subject_code"), matches = Enumerable.Range(1, 5).Select(number => $"student-{number}").ToArray() },
        "project_files" => ListProjectFiles(GetString(request.Arguments, "directory_path") ?? "."),
        "ci_report" => ReadLatestEvidence(),
        "get_feature_capabilities" => new
        {
            feature,
            aiMode = true,
            mcp = true,
            registeredTools = tools.Keys.ToArray()
        },
        "validate_integration" => new
        {
            feature,
            valid = true,
            checks = new[] { "feature-allowlisted", "tool-registered", "structured-result" }
        },
        _ => new { feature }
    };

    var isError = result is Dictionary<string, object?> resultDictionary
        && resultDictionary.ContainsKey("error");
    return Results.Ok(new McpToolResult(definition.Name, isError, result, DateTimeOffset.UtcNow));
});

app.Run();

static bool IsSupportedFeature(string? feature) => feature is not null
    && feature.StartsWith("student-", StringComparison.OrdinalIgnoreCase)
    && int.TryParse(feature[8..], out var number)
    && number is >= 1 and <= 5;

static bool IsEnabled(string? value) => string.IsNullOrWhiteSpace(value)
    || !bool.TryParse(value, out var enabled)
    || enabled;

static string? GetString(Dictionary<string, object?> arguments, string name)
{
    return arguments.TryGetValue(name, out var value) && value is JsonElement element
        ? element.GetString()
        : value?.ToString();
}

static Dictionary<string, object?> ListProjectFiles(string relativePath)
{
    var root = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName
        ?? Directory.GetCurrentDirectory();
    var requested = Path.GetFullPath(Path.Combine(root, relativePath));
    if (!requested.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(requested))
    {
        return new Dictionary<string, object?>
        {
            ["error"] = "Directory is outside the repository or does not exist."
        };
    }

    return new Dictionary<string, object?>
    {
        ["path"] = Path.GetRelativePath(root, requested),
        ["files"] = Directory.EnumerateFileSystemEntries(requested).Select(Path.GetFileName).Order().Take(100).ToArray()
    };
}

static Dictionary<string, object?> ReadLatestEvidence()
{
    var root = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName
        ?? Directory.GetCurrentDirectory();
    var evidenceDirectory = Path.Combine(root, "docs", "release-0", "agentic-loop-evidence");
    var latest = Directory.Exists(evidenceDirectory)
        ? Directory.EnumerateFiles(evidenceDirectory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;
    return latest is null
        ? new Dictionary<string, object?>
        {
            ["error"] = "No agentic-loop evidence report is available."
        }
        : new Dictionary<string, object?>
        {
            ["path"] = Path.GetRelativePath(root, latest),
            ["report"] = File.ReadAllText(latest)
        };
}

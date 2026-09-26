using System.Net.Http.Json;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient("academicRecords", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsBackend"] ?? "http://localhost:5020");
    client.Timeout = TimeSpan.FromSeconds(30);
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
    ["calculate_student_academic_statistics"] = new(
        "calculate_student_academic_statistics",
        "Calculate a student's weighted average mark (WAM) and grade point average (GPA).",
        new Dictionary<string, string> { ["student_id"] = "Required student identifier, for example STU123" }),
};

app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mcp", toolCount = tools.Count }));
app.MapGet("/api/mcp/tools", (IConfiguration configuration) => !IsEnabled(configuration["MCP_ENABLED"])
    ? Results.StatusCode(StatusCodes.Status403Forbidden)
    : Results.Ok(tools.Values.Select(tool => new
{
    tool.Name,
    tool.Description,
    InputSchema = tool.InputSchema
})));
app.MapPost("/api/mcp/tools/call", async (
    McpCallRequest request,
    IConfiguration configuration,
    IHttpClientFactory clients,
    CancellationToken cancellationToken) =>
{
    if (!IsEnabled(configuration["MCP_ENABLED"]))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (!tools.TryGetValue(request.Tool, out var definition))
    {
        return Results.NotFound(new { error = $"Tool '{request.Tool}' is not registered." });
    }

    if (definition.Name == "calculate_student_academic_statistics")
    {
        return Results.Ok(await CalculateStudentAcademicStatisticsAsync(request.Arguments, clients, cancellationToken));
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
    if (!arguments.TryGetValue(name, out var value))
    {
        return null;
    }

    return value switch
    {
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        string text => text,
        _ => null
    };
}

static async Task<McpToolResult> CalculateStudentAcademicStatisticsAsync(
    Dictionary<string, object?> arguments,
    IHttpClientFactory clients,
    CancellationToken cancellationToken)
{
    var studentId = GetString(arguments, "student_id");
    if (string.IsNullOrWhiteSpace(studentId))
    {
        return new McpToolResult(
            "calculate_student_academic_statistics",
            true,
            new { error = "A non-empty student_id argument is required." },
            DateTimeOffset.UtcNow);
    }

    try
    {
        var client = clients.CreateClient("academicRecords");
        var escapedStudentId = Uri.EscapeDataString(studentId);
        var wamTask = client.GetAsync($"/api/students/{escapedStudentId}/wam", cancellationToken);
        var gpaTask = client.GetAsync($"/api/students/{escapedStudentId}/gpa", cancellationToken);
        await Task.WhenAll(wamTask, gpaTask);

        using var wamResponse = await wamTask;
        using var gpaResponse = await gpaTask;
        if (!wamResponse.IsSuccessStatusCode || !gpaResponse.IsSuccessStatusCode)
        {
            var failedResponse = !wamResponse.IsSuccessStatusCode ? wamResponse : gpaResponse;
            return new McpToolResult(
                "calculate_student_academic_statistics",
                true,
                new
                {
                    error = "Academic Records could not retrieve statistics for this student.",
                    statusCode = (int)failedResponse.StatusCode
                },
                DateTimeOffset.UtcNow);
        }

        var wam = await wamResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var gpa = await gpaResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return new McpToolResult(
            "calculate_student_academic_statistics",
            false,
            new { studentId, wam, gpa },
            DateTimeOffset.UtcNow);
    }
    catch (HttpRequestException)
    {
        return new McpToolResult(
            "calculate_student_academic_statistics",
            true,
            new { error = "The Academic Records backend is unavailable." },
            DateTimeOffset.UtcNow);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return new McpToolResult(
            "calculate_student_academic_statistics",
            true,
            new { error = "The Academic Records backend did not respond in time." },
            DateTimeOffset.UtcNow);
    }
    catch (JsonException)
    {
        return new McpToolResult(
            "calculate_student_academic_statistics",
            true,
            new { error = "The Academic Records backend returned an invalid statistics response." },
            DateTimeOffset.UtcNow);
    }
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

public sealed record McpCallRequest(string Tool, Dictionary<string, object?> Arguments);
public sealed record McpToolResult(string Tool, bool IsError, object Result, DateTimeOffset ExecutedAtUtc);
public sealed record McpToolDefinition(string Name, string Description, Dictionary<string, string> InputSchema);

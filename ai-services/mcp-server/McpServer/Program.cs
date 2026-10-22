using McpServer.Contracts;
using McpServer.Tools;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddCampusPulseSecurity(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<McpAuthorizationHeaderHandler>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient("courseManagement", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Services:CourseManagementBackend"]
        ?? "http://localhost:5118");
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<McpAuthorizationHeaderHandler>();
builder.Services.AddHttpClient("academicRecords", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsBackend"] ?? "http://localhost:5020");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<McpAuthorizationHeaderHandler>();
builder.Services.AddHttpClient("campusHousing", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CampusHousingDatabase"] ?? "http://localhost:5093");
    client.Timeout = TimeSpan.FromSeconds(10);
}).ForwardCampusPulseAuthorization();
builder.Services.AddMcpTools();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.MapGet("/health", (IEnumerable<IMcpTool> tools) => Results.Ok(new
{
    status = "healthy",
    service = "mcp",
    toolCount = tools.Count()
})).AllowAnonymous();
app.MapGet("/api/mcp/tools", (
    IConfiguration configuration,
    IEnumerable<IMcpTool> tools) =>
{
    if (!IsEnabled(configuration["MCP_ENABLED"]))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(tools.Select(tool => new
    {
        tool.Name,
        tool.Description,
        tool.InputSchema,
        tool.Owner
    }));
}).RequireAuthorization();
app.MapPost("/api/mcp/tools/call", async Task<IResult> (
    McpCallRequest request,
    IConfiguration configuration,
    IEnumerable<IMcpTool> tools,
    CancellationToken cancellationToken) =>
{
    if (!IsEnabled(configuration["MCP_ENABLED"]))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var tool = tools.FirstOrDefault(candidate =>
        string.Equals(
            candidate.Name,
            request.Tool,
            StringComparison.OrdinalIgnoreCase));
    if (tool is null)
    {
        return Results.NotFound(new { error = $"Tool '{request.Tool}' is not registered." });
    }

    var executionRequest = request with
    {
        RegisteredTools = tools
            .Select(candidate => new McpToolDescriptor(
                candidate.Name,
                candidate.Owner,
                candidate.Description,
                candidate.InputSchema))
            .ToArray()
    };
    return await tool.ExecuteAsync(executionRequest, cancellationToken);
}).RequireAuthorization();

app.Run();

static bool IsEnabled(string? value) => string.IsNullOrWhiteSpace(value)
    || !bool.TryParse(value, out var enabled)
    || enabled;

using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Generation;
using FinancialAidBilling.Backend.Models;
using FinancialAidBilling.Backend.Services;
using System.Net.Http.Json;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCampusPulseSecurity(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddTransient<DatabaseStartupRetryHandler>();
builder.Services.AddHttpClient("billingDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:InvoiceDatabase"] ?? "http://financialaidbilling.database:8080");
}).AddHttpMessageHandler<DatabaseStartupRetryHandler>().ForwardCampusPulseAuthorization();
builder.Services.AddHttpClient("academicRecords", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsBackend"] ?? "http://academicrecords.backend:8080");
}).ForwardCampusPulseAuthorization();
builder.Services.AddHttpClient("mcp", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
}).AddHttpMessageHandler<McpAuthorizationHeaderHandler>();
builder.Services.AddMcpClient(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ForwardCampusPulseAuthorization();
builder.Services.AddScoped<ScholarshipAcademicService>();
builder.Services.AddScoped<GeminiScholarshipService>();
builder.Services.AddHttpClient<IRagServerClient, RagServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
}).ForwardCampusPulseAuthorization();
builder.Services.AddSingleton<RagCitationParser>();
builder.Services.AddScoped<RagKnowledgeService>();
builder.Services.AddScoped<IFinancialAidKnowledgeService, FinancialAidKnowledgeService>();
builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddMultiAgentCaller(builder.Configuration);

var app = builder.Build();
app.MapMultiAgentCaller("financialaidbilling", "billingDatabase", "/api/scholarships");

app.MapOpenApi();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/students", async (
    IHttpClientFactory clients,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/students");
    if (context.Request.Headers.Authorization.Count > 0)
    {
        request.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToArray());
    }

    var response = await clients.CreateClient("academicRecords").SendAsync(request, cancellationToken);
    return await response.ToResultAsync(cancellationToken);
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

var invoices = app.MapGroup("/api/invoices");

invoices.MapGet("/", async (HttpContext context, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await ForwardAuthorizedBillingReadAsync(context, clients, HttpMethod.Get, "/api/invoices", cancellationToken));

invoices.MapGet("/{id:int}", async (int id, HttpContext context, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await ForwardAuthorizedBillingReadAsync(context, clients, HttpMethod.Get, $"/api/invoices/{id}", cancellationToken));

invoices.MapPost("/", async (InvoiceRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PostAsJsonAsync("/api/invoices", request, cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

invoices.MapPut("/{id:int}", async (int id, InvoiceRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PutAsJsonAsync($"/api/invoices/{id}", request, cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

invoices.MapDelete("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").DeleteAsync($"/api/invoices/{id}", cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

var payments = app.MapGroup("/api/payments");
payments.MapGet("/", async (HttpContext context, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await ForwardAuthorizedBillingReadAsync(context, clients, HttpMethod.Get, "/api/payments", cancellationToken))
    .RequireAuthorization();
payments.MapGet("/{id:int}", async (int id, HttpContext context, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await ForwardAuthorizedBillingReadAsync(context, clients, HttpMethod.Get, $"/api/payments/{id}", cancellationToken))
    .RequireAuthorization();
payments.MapPost("/", async (PaymentRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PostAsJsonAsync("/api/payments", request, cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StudentOnlyPolicy);
payments.MapPut("/{id:int}", async (int id, PaymentRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PutAsJsonAsync($"/api/payments/{id}", request, cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);
payments.MapDelete("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").DeleteAsync($"/api/payments/{id}", cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);


var scholarships = app.MapGroup("/api/scholarships");
scholarships.MapGet("/", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync("/api/scholarships", cancellationToken)).ToResultAsync(cancellationToken));
scholarships.MapGet("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync($"/api/scholarships/{id}", cancellationToken)).ToResultAsync(cancellationToken));
scholarships.MapPost("/", async (ScholarshipRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PostAsJsonAsync("/api/scholarships", request, cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);
scholarships.MapPut("/{id:int}", async (int id, ScholarshipRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PutAsJsonAsync($"/api/scholarships/{id}", request, cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);
scholarships.MapDelete("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").DeleteAsync($"/api/scholarships/{id}", cancellationToken)).ToResultAsync(cancellationToken))
    .RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

scholarships.MapPost("/recommendations/student/{userId:int}", async (
    int userId, HttpContext context, IHttpClientFactory clients, GeminiScholarshipService service, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!context.User.IsInRole("Student") || !context.User.CanAccessUser(userId))
    {
        return Results.Forbid();
    }
    if (!configuration.GetValue("Mcp:Enabled", true))
    {
        return Results.Problem(statusCode: 403, title: "MCP is disabled in this deployment; scholarship recommendations are unavailable.");
    }

    try
    {
        var profile = await clients.CreateClient("academicRecords")
            .GetFromJsonAsync<AcademicStudentProfile>($"/api/students/by-user/{userId}", cancellationToken);
        if (profile is null)
        {
            return Results.NotFound("Student information was not found.");
        }

        var yearLevel = profile.EnrollmentDate is null
            ? 1
            : Math.Max(1, DateTime.UtcNow.Year - profile.EnrollmentDate.Value.Year + 1);
        var student = new StudentProfile(
            profile.UserId,
            $"{profile.FirstName} {profile.LastName}",
            string.IsNullOrWhiteSpace(profile.Major) ? "Undeclared" : profile.Major,
            yearLevel,
            profile.StudentId);

        return Results.Ok(await service.RecommendAsync(student, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Student information or the scholarship recommendation service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (McpResponseException)
    {
        return Results.Problem("Academic statistics could not be verified through MCP. Check that MCP and Academic Records are running.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Problem("A scholarship dependency returned an invalid response.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem("The scholarship recommendation service did not respond in time.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
}).RequireAuthorization(CampusPulseSecurityExtensions.StudentOnlyPolicy);

var assistant = app.MapGroup("/api/assistant");

assistant.MapPost("/mcp/integration", async (IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!configuration.GetValue("Mcp:Enabled", true))
    {
        return Results.Problem(statusCode: 403, title: "MCP is disabled in this deployment.");
    }

    try
    {
        return await (await clients.CreateClient("mcp").PostAsJsonAsync(
            "/api/mcp/tools/call",
            new McpToolCallRequest("validate_integration", new Dictionary<string, object?> { ["feature"] = "student-4" }),
            cancellationToken)).ToResultAsync(cancellationToken);
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            "The shared MCP server is unavailable. Check that it is running and reachable at the configured Services:McpServer address.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            "The shared MCP server did not respond in time.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

assistant.MapPost("/rag/scholarships", async (
    RagQuestionRequest request,
    IFinancialAidKnowledgeService knowledge,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!configuration.GetValue("Rag:Enabled", true))
    {
        return Results.Problem(statusCode: 403, title: "RAG is disabled in this deployment.");
    }

    if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1000)
    {
        return Results.BadRequest(new { error = "Ask a scholarship question of up to 1000 characters." });
    }

    try
    {
        return Results.Ok(await knowledge.AskScholarshipsAsync(request, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Problem("A scholarship knowledge dependency returned an invalid response.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            "The scholarship database or shared RAG server is unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            "A shared service did not respond in time.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapPost("/api/ai/knowledge", async (
    RagQuestionRequest request,
    IFinancialAidKnowledgeService knowledge,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!configuration.GetValue("Rag:Enabled", true))
    {
        return Results.Problem(statusCode: 403, title: "RAG is disabled in this deployment.");
    }

    if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1000)
    {
        return Results.BadRequest(new { error = "Ask a question of up to 1000 characters." });
    }

    try
    {
        return Results.Ok(await knowledge.AskAsync(request, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Problem("A scholarship knowledge dependency returned an invalid response.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            "The scholarship database or shared RAG server is unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem("The knowledge assistant did not respond in time.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Run();

static async Task<IResult> ForwardAuthorizedBillingReadAsync(
    HttpContext context,
    IHttpClientFactory clients,
    HttpMethod method,
    string path,
    CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(method, path);
    if (context.Request.Headers.Authorization.Count > 0)
    {
        request.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToArray());
    }

    using var response = await clients.CreateClient("billingDatabase").SendAsync(request, cancellationToken);
    return await response.ToResultAsync(cancellationToken);
}

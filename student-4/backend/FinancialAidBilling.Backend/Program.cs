using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using FinancialAidBilling.Backend.Models;
using FinancialAidBilling.Backend.Services;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddTransient<DatabaseStartupRetryHandler>();
builder.Services.AddHttpClient("billingDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:InvoiceDatabase"] ?? "http://financialaidbilling.database:8080");
}).AddHttpMessageHandler<DatabaseStartupRetryHandler>();
builder.Services.AddHttpClient("academicRecords", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsBackend"] ?? "http://academicrecords.backend:8080");
});
builder.Services.AddHttpClient("mcp", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
});
builder.Services.AddHttpClient("rag", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddScoped<GeminiScholarshipService>();
builder.Services.AddHttpClient<IRagServerClient, RagServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddHttpClient<IMcpServerClient, McpServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
});
builder.Services.AddSingleton<RagCitationParser>();
builder.Services.AddScoped<RagKnowledgeService>();
builder.Services.AddScoped<IFinancialAidKnowledgeService, FinancialAidKnowledgeService>();
var geminiApiKey = builder.Configuration["GEMINI_API_KEY"];

builder.Services.AddHttpClient("gemini", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(90);

    if (!string.IsNullOrWhiteSpace(geminiApiKey))
    {
        client.DefaultRequestHeaders.Add("x-goog-api-key", geminiApiKey);
    }
});

var app = builder.Build();

app.MapOpenApi();
app.UseCors();

app.MapGet("/api/students", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("academicRecords").GetAsync("/api/students", cancellationToken))
        .ToResultAsync(cancellationToken));

var invoices = app.MapGroup("/api/invoices");

invoices.MapGet("/", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync("/api/invoices", cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapGet("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync($"/api/invoices/{id}", cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapPost("/", async (InvoiceRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PostAsJsonAsync("/api/invoices", request, cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapPut("/{id:int}", async (int id, InvoiceRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PutAsJsonAsync($"/api/invoices/{id}", request, cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapDelete("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").DeleteAsync($"/api/invoices/{id}", cancellationToken)).ToResultAsync(cancellationToken));

var payments = app.MapGroup("/api/payments");
payments.MapGet("/", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync("/api/payments", cancellationToken)).ToResultAsync(cancellationToken));
payments.MapGet("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync($"/api/payments/{id}", cancellationToken)).ToResultAsync(cancellationToken));
payments.MapPost("/", async (PaymentRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PostAsJsonAsync("/api/payments", request, cancellationToken)).ToResultAsync(cancellationToken));
payments.MapPut("/{id:int}", async (int id, PaymentRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PutAsJsonAsync($"/api/payments/{id}", request, cancellationToken)).ToResultAsync(cancellationToken));
payments.MapDelete("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").DeleteAsync($"/api/payments/{id}", cancellationToken)).ToResultAsync(cancellationToken));

var scholarships = app.MapGroup("/api/scholarships");
scholarships.MapGet("/", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync("/api/scholarships", cancellationToken)).ToResultAsync(cancellationToken));
scholarships.MapGet("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").GetAsync($"/api/scholarships/{id}", cancellationToken)).ToResultAsync(cancellationToken));
scholarships.MapPost("/", async (ScholarshipRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PostAsJsonAsync("/api/scholarships", request, cancellationToken)).ToResultAsync(cancellationToken));
scholarships.MapPut("/{id:int}", async (int id, ScholarshipRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").PutAsJsonAsync($"/api/scholarships/{id}", request, cancellationToken)).ToResultAsync(cancellationToken));
scholarships.MapDelete("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("billingDatabase").DeleteAsync($"/api/scholarships/{id}", cancellationToken)).ToResultAsync(cancellationToken));

scholarships.MapPost("/recommendations/student/{userId:int}", async (int userId, IHttpClientFactory clients, GeminiScholarshipService service, CancellationToken cancellationToken) =>
{
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
            yearLevel);

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
});

var assistant = app.MapGroup("/api/assistant");

assistant.MapPost("/mcp/integration", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
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
            "The shared MCP server is unavailable. Start it on the host at http://localhost:5268 before using this feature.",
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
    IHttpClientFactory clients,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1000)
    {
        return Results.BadRequest(new { error = "Ask a scholarship question of up to 1000 characters." });
    }

    try
    {
        using var scholarshipsResponse = await clients.CreateClient("billingDatabase")
            .GetAsync("/api/scholarships", cancellationToken);
        if (!scholarshipsResponse.IsSuccessStatusCode)
        {
            return await scholarshipsResponse.ToResultAsync(cancellationToken);
        }

        var scholarships = await scholarshipsResponse.Content.ReadFromJsonAsync<List<ScholarshipResponse>>(cancellationToken)
            ?? [];
        var documents = scholarships.Select(scholarship => new RagDocument(
            $"scholarship-{scholarship.Id}",
            $"{scholarship.Name}. {scholarship.Description} Award amount: {scholarship.AwardAmount:C0}. "
                + $"Minimum GPA: {scholarship.MinimumGpa:F2}. Eligible major: {scholarship.EligibleMajor}. "
                + $"Minimum year level: {scholarship.MinimumYearLevel}. Application deadline: {scholarship.ApplicationDeadline:MMMM d, yyyy}. "
                + $"Applications are {(scholarship.IsActive ? "open" : "closed")}.",
            new Dictionary<string, string> { ["scholarshipName"] = scholarship.Name }))
            .ToList();

        using var ingestResponse = await clients.CreateClient("rag").PostAsJsonAsync(
            "/api/rag/financialaidbilling/documents",
            new RagIngestRequest(documents),
            cancellationToken);
        if (!ingestResponse.IsSuccessStatusCode)
        {
            return await ingestResponse.ToResultAsync(cancellationToken);
        }

        return await (await clients.CreateClient("rag").PostAsJsonAsync(
            "/api/rag/financialaidbilling/answer",
            new RagAnswerRequest(request.Question.Trim(), 4, "You are the Financial Aid & Billing scholarship assistant. Answer only from the scholarship catalogue and cite your sources.", null),
            cancellationToken)).ToResultAsync(cancellationToken);
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            "The shared RAG server is unavailable. Start it on the host at http://localhost:5267 before using this feature.",
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
    CancellationToken cancellationToken) =>
{
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
    catch (HttpRequestException)
    {
        return Results.Problem(
            "The shared RAG server is unavailable. Start it on the host at http://localhost:5267 before using this feature.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem("The knowledge assistant did not respond in time.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

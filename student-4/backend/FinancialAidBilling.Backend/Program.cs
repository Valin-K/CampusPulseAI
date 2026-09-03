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
builder.Services.AddScoped<GeminiScholarshipService>();
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

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

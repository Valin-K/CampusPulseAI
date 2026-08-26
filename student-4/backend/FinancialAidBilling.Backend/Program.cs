using FinancialAidBilling.Backend.Models;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddHttpClient("invoiceDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:InvoiceDatabase"] ?? "http://financialaidbilling.database:8080");
});
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

var invoices = app.MapGroup("/api/invoices");

invoices.MapGet("/", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("invoiceDatabase").GetAsync("/api/invoices", cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapGet("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("invoiceDatabase").GetAsync($"/api/invoices/{id}", cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapPost("/", async (InvoiceRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("invoiceDatabase").PostAsJsonAsync("/api/invoices", request, cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapPut("/{id:int}", async (int id, InvoiceRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("invoiceDatabase").PutAsJsonAsync($"/api/invoices/{id}", request, cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapDelete("/{id:int}", async (int id, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await (await clients.CreateClient("invoiceDatabase").DeleteAsync($"/api/invoices/{id}", cancellationToken)).ToResultAsync(cancellationToken));

invoices.MapPost("/{id:int}/explanation", async (int id, IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"]))
    {
        return Results.Problem("GEMINI_API_KEY is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var invoiceResponse = await clients.CreateClient("invoiceDatabase").GetAsync($"/api/invoices/{id}", cancellationToken);
    if (invoiceResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound();
    }

    if (!invoiceResponse.IsSuccessStatusCode)
    {
        return Results.Problem("The invoice database service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceResponse>(cancellationToken: cancellationToken);
    if (invoice is null)
    {
        return Results.Problem("The invoice response was invalid.", statusCode: StatusCodes.Status502BadGateway);
    }

    var prompt = $"Explain this university tuition or fee invoice in clear, concise language. Do not invent policies, discounts, or payment plans. Student ID: {invoice.StudentId}; description: {invoice.Description}; amount: {invoice.Amount:C}; issue date: {invoice.IssueDate:yyyy-MM-dd}; due date: {invoice.DueDate:yyyy-MM-dd}; status: {invoice.Status}.";
    var request = new GeminiGenerateRequest([new GeminiContent([new GeminiPart(prompt)])]);
    var model = configuration["Gemini:Model"] ?? "gemini-2.0-flash";

    try
    {
        var response = await clients.CreateClient("gemini").PostAsJsonAsync($"v1beta/models/{model}:generateContent", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Problem($"Gemini could not generate an explanation: {detail}", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await response.Content.ReadFromJsonAsync<GeminiGenerateResponse>(cancellationToken: cancellationToken);
        var explanation = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        return string.IsNullOrWhiteSpace(explanation)
            ? Results.Problem("Gemini returned an empty explanation.", statusCode: StatusCodes.Status502BadGateway)
            : Results.Ok(new InvoiceExplanationResponse(id, explanation));
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Gemini is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

using System.Net.Http.Json;

namespace FinancialAidBilling.Backend.Models;

public sealed record InvoiceRequest(
    string StudentId,
    string Description,
    decimal Amount,
    DateOnly IssueDate,
    DateOnly DueDate,
    InvoiceStatus Status);

public sealed record InvoiceResponse(
    int Id,
    string StudentId,
    string Description,
    decimal Amount,
    DateOnly IssueDate,
    DateOnly DueDate,
    InvoiceStatus Status);

public enum InvoiceStatus
{
    Outstanding,
    Paid,
    Overdue
}

public sealed record OllamaGenerateRequest(string Model, string Prompt, bool Stream);

public sealed record OllamaGenerateResponse(string? Response);

public sealed record InvoiceExplanationResponse(int InvoiceId, string Explanation);

public static class HttpResponseMessageExtensions
{
    public static async Task<IResult> ToResultAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
    }
}

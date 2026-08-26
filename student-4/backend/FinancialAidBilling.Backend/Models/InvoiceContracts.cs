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

public sealed record GeminiGenerateRequest(IReadOnlyList<GeminiContent> Contents);

public sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);

public sealed record GeminiPart(string Text);

public sealed record GeminiGenerateResponse(IReadOnlyList<GeminiCandidate>? Candidates);

public sealed record GeminiCandidate(GeminiContent? Content);

public sealed record InvoiceExplanationResponse(int InvoiceId, string Explanation);

public static class HttpResponseMessageExtensions
{
    public static async Task<IResult> ToResultAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
    }
}

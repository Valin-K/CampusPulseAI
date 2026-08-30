using System.Net.Http.Json;

namespace FinancialAidBilling.Backend.Models;

public sealed record InvoiceRequest(
    int StudentId,
    string Description,
    decimal Amount,
    DateOnly IssueDate,
    DateOnly DueDate,
    InvoiceStatus Status);

public sealed record InvoiceResponse(
    int Id,
    int StudentId,
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

public sealed record PaymentRequest(
    int InvoiceId,
    int StudentId,
    decimal Amount,
    DateTime PaidAtUtc,
    PaymentMethod Method,
    string Reference);

public sealed record PaymentResponse(
    int Id,
    int InvoiceId,
    int StudentId,
    decimal Amount,
    DateTime PaidAtUtc,
    PaymentMethod Method,
    string Reference);

public enum PaymentMethod
{
    Card,
    BankTransfer,
    FinancialAid
}

public sealed record ScholarshipRequest(
    string Name,
    string Description,
    decimal AwardAmount,
    decimal MinimumGpa,
    string EligibleMajor,
    int MinimumYearLevel,
    DateOnly ApplicationDeadline,
    bool IsActive);

public sealed record ScholarshipResponse(
    int Id,
    string Name,
    string Description,
    decimal AwardAmount,
    decimal MinimumGpa,
    string EligibleMajor,
    int MinimumYearLevel,
    DateOnly ApplicationDeadline,
    bool IsActive);

public sealed record StudentProfile(
    int StudentId,
    string Name,
    string Major,
    decimal Gpa,
    int YearLevel,
    bool IsFirstGeneration,
    IReadOnlyList<string> Interests);

public sealed record GeminiGenerateRequest(IReadOnlyList<GeminiContent> Contents);

public sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);

public sealed record GeminiPart(string Text);

public sealed record GeminiGenerateResponse(IReadOnlyList<GeminiCandidate>? Candidates);

public sealed record GeminiCandidate(GeminiContent? Content);

public sealed record ScholarshipRecommendationResponse(int StudentId, int MatchCount, string Recommendation);

public static class HttpResponseMessageExtensions
{
    public static async Task<IResult> ToResultAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
    }
}

namespace FinancialAidBilling.Frontend.Models;

public sealed class InvoiceInput
{
    public int StudentId { get; set; } = 4;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Outstanding;

    public InvoiceRequest ToRequest() => new(StudentId, Description, Amount, IssueDate, DueDate, Status);

    public static InvoiceInput From(InvoiceResponse invoice) => new()
    {
        StudentId = invoice.StudentId,
        Description = invoice.Description,
        Amount = invoice.Amount,
        IssueDate = invoice.IssueDate,
        DueDate = invoice.DueDate,
        Status = invoice.Status
    };
}

public sealed record InvoiceRequest(int StudentId, string Description, decimal Amount, DateOnly IssueDate, DateOnly DueDate, InvoiceStatus Status);

public sealed record InvoiceResponse(int Id, int StudentId, string Description, decimal Amount, DateOnly IssueDate, DateOnly DueDate, InvoiceStatus Status);

public sealed class PaymentInput
{
    public int InvoiceId { get; set; }
    public int StudentId { get; set; } = 4;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Card;
    public string Reference { get; set; } = $"PAY-{DateTime.UtcNow:yyyyMMddHHmm}";

    public PaymentRequest ToRequest() => new(InvoiceId, StudentId, Amount, DateTime.UtcNow, Method, Reference);
}

public sealed record PaymentRequest(int InvoiceId, int StudentId, decimal Amount, DateTime PaidAtUtc, PaymentMethod Method, string Reference);

public sealed record PaymentResponse(int Id, int InvoiceId, int StudentId, decimal Amount, DateTime PaidAtUtc, PaymentMethod Method, string Reference);

public sealed class ScholarshipInput
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal AwardAmount { get; set; }
    public decimal MinimumGpa { get; set; } = 3m;
    public string EligibleMajor { get; set; } = "Any";
    public int MinimumYearLevel { get; set; } = 1;
    public DateOnly ApplicationDeadline { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
    public bool IsActive { get; set; } = true;

    public ScholarshipRequest ToRequest() => new(Name, Description, AwardAmount, MinimumGpa, EligibleMajor, MinimumYearLevel, ApplicationDeadline, IsActive);

    public static ScholarshipInput From(ScholarshipResponse scholarship) => new()
    {
        Name = scholarship.Name,
        Description = scholarship.Description,
        AwardAmount = scholarship.AwardAmount,
        MinimumGpa = scholarship.MinimumGpa,
        EligibleMajor = scholarship.EligibleMajor,
        MinimumYearLevel = scholarship.MinimumYearLevel,
        ApplicationDeadline = scholarship.ApplicationDeadline,
        IsActive = scholarship.IsActive
    };
}

public sealed record ScholarshipRequest(string Name, string Description, decimal AwardAmount, decimal MinimumGpa, string EligibleMajor, int MinimumYearLevel, DateOnly ApplicationDeadline, bool IsActive);

public sealed record ScholarshipResponse(int Id, string Name, string Description, decimal AwardAmount, decimal MinimumGpa, string EligibleMajor, int MinimumYearLevel, DateOnly ApplicationDeadline, bool IsActive);

public sealed record ScholarshipRecommendationResponse(
    int StudentId, int MatchCount, string Recommendation, ScholarshipAcademicContext AcademicContext);

public sealed record ScholarshipAcademicContext(
    string StudentId, decimal? Gpa, int GpaScale, string GpaEligibility, string Tool, DateTimeOffset ExecutedAtUtc);

public sealed record RagQuestionRequest(string Question);

public sealed record McpIntegrationResponse(string Tool, bool IsError, McpIntegrationResult Result, DateTimeOffset ExecutedAtUtc);

public sealed record McpIntegrationResult(string Feature, bool Valid, IReadOnlyList<string> Checks);

public sealed record RagAnswerResponse(
    string Collection,
    string Question,
    string Status,
    string? Answer,
    double Confidence,
    IReadOnlyList<RagSource> Sources,
    string Embedder,
    string? Detail,
    DateTimeOffset ExecutedAtUtc);

public sealed record RagSource(int Rank, string DocumentId, int ChunkIndex, double Score, string Text, Dictionary<string, string>? Metadata);

public enum InvoiceStatus
{
    Outstanding,
    Paid,
    Overdue
}

public enum PaymentMethod
{
    Card,
    BankTransfer,
    FinancialAid
}

namespace FinancialAidBilling.Frontend.Models;

public sealed class InvoiceInput
{
    public string StudentId { get; set; } = string.Empty;
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

public sealed record InvoiceRequest(string StudentId, string Description, decimal Amount, DateOnly IssueDate, DateOnly DueDate, InvoiceStatus Status);

public sealed record InvoiceResponse(int Id, string StudentId, string Description, decimal Amount, DateOnly IssueDate, DateOnly DueDate, InvoiceStatus Status);

public sealed record InvoiceExplanationResponse(int InvoiceId, string Explanation);

public enum InvoiceStatus
{
    Outstanding,
    Paid,
    Overdue
}

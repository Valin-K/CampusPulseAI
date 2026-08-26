namespace FinancialAidBilling.Database.Models;

public sealed class Invoice
{
    public int Id { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceStatus Status { get; set; }

    public void UpdateFrom(InvoiceRequest request)
    {
        StudentId = request.StudentId.Trim();
        Description = request.Description.Trim();
        Amount = request.Amount;
        IssueDate = request.IssueDate;
        DueDate = request.DueDate;
        Status = request.Status;
    }

    public InvoiceResponse ToResponse() => new(Id, StudentId, Description, Amount, IssueDate, DueDate, Status);
}

public enum InvoiceStatus
{
    Outstanding,
    Paid,
    Overdue
}

public sealed record InvoiceRequest(
    string StudentId,
    string Description,
    decimal Amount,
    DateOnly IssueDate,
    DateOnly DueDate,
    InvoiceStatus Status)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(StudentId) || StudentId.Trim().Length > 32)
        {
            return "Student ID is required and must be 32 characters or fewer.";
        }

        if (string.IsNullOrWhiteSpace(Description) || Description.Trim().Length > 200)
        {
            return "Description is required and must be 200 characters or fewer.";
        }

        if (Amount <= 0)
        {
            return "Amount must be greater than zero.";
        }

        if (DueDate < IssueDate)
        {
            return "Due date cannot be before issue date.";
        }

        return null;
    }

    public Invoice ToInvoice() => new()
    {
        StudentId = StudentId.Trim(),
        Description = Description.Trim(),
        Amount = Amount,
        IssueDate = IssueDate,
        DueDate = DueDate,
        Status = Status
    };
}

public sealed record InvoiceResponse(
    int Id,
    string StudentId,
    string Description,
    decimal Amount,
    DateOnly IssueDate,
    DateOnly DueDate,
    InvoiceStatus Status);

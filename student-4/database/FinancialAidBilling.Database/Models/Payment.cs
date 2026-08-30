namespace FinancialAidBilling.Database.Models;

public sealed class Payment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int StudentId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public PaymentMethod Method { get; set; }
    public string Reference { get; set; } = string.Empty;

    public void UpdateFrom(PaymentRequest request)
    {
        InvoiceId = request.InvoiceId;
        StudentId = request.StudentId;
        Amount = request.Amount;
        PaidAtUtc = request.PaidAtUtc;
        Method = request.Method;
        Reference = request.Reference.Trim();
    }

    public PaymentResponse ToResponse() => new(Id, InvoiceId, StudentId, Amount, PaidAtUtc, Method, Reference);
}

public enum PaymentMethod
{
    Card,
    BankTransfer,
    FinancialAid
}

public sealed record PaymentRequest(
    int InvoiceId,
    int StudentId,
    decimal Amount,
    DateTime PaidAtUtc,
    PaymentMethod Method,
    string Reference)
{
    public string? Validate()
    {
        if (InvoiceId <= 0 || StudentId <= 0)
        {
            return "Invoice ID and student ID must be greater than zero.";
        }

        if (Amount <= 0)
        {
            return "Payment amount must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(Reference) || Reference.Trim().Length > 64)
        {
            return "Reference is required and must be 64 characters or fewer.";
        }

        return null;
    }

    public Payment ToPayment()
    {
        var payment = new Payment();
        payment.UpdateFrom(this);
        return payment;
    }
}

public sealed record PaymentResponse(
    int Id,
    int InvoiceId,
    int StudentId,
    decimal Amount,
    DateTime PaidAtUtc,
    PaymentMethod Method,
    string Reference);
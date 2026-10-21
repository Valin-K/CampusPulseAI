using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAidBilling.Database.Data;

public static class InvoiceSeedData
{
    public static async Task InitializeAsync(FinancialAidBillingDbContext database)
    {
        if (!await database.Invoices.AnyAsync())
        {
            database.Invoices.AddRange(
                Invoice(1, "Fall 2026 undergraduate tuition", 4850m, new(2026, 8, 1), new(2026, 8, 25), InvoiceStatus.Outstanding),
                Invoice(2, "Student services and technology fee", 425m, new(2026, 8, 1), new(2026, 8, 25), InvoiceStatus.Outstanding),
                Invoice(3, "Campus housing deposit", 750m, new(2026, 5, 15), new(2026, 6, 15), InvoiceStatus.Paid),
                Invoice(4, "Spring 2026 undergraduate tuition", 4725m, new(2026, 1, 2), new(2026, 1, 20), InvoiceStatus.Paid),
                Invoice(5, "Engineering laboratory fee", 310m, new(2026, 1, 2), new(2026, 1, 20), InvoiceStatus.Paid),
                Invoice(6, "University health insurance", 980m, new(2025, 8, 1), new(2025, 8, 25), InvoiceStatus.Overdue),
                Invoice(7, "Library replacement charge", 85m, new(2026, 3, 12), new(2026, 4, 12), InvoiceStatus.Paid),
                Invoice(8, "Summer research course tuition", 1650m, new(2026, 5, 1), new(2026, 5, 20), InvoiceStatus.Paid),
                Invoice(9, "Parking permit", 240m, new(2026, 8, 1), new(2026, 8, 25), InvoiceStatus.Outstanding),
                Invoice(10, "Graduation application fee", 125m, new(2026, 9, 1), new(2026, 10, 1), InvoiceStatus.Outstanding));
        }

        if (!await database.Payments.AnyAsync())
        {
            database.Payments.AddRange(
                Payment(1, 4, 4725m, new(2026, 1, 12, 15, 30, 0, DateTimeKind.Utc), PaymentMethod.BankTransfer, "BANK-260112-004"),
                Payment(2, 5, 310m, new(2026, 1, 13, 18, 15, 0, DateTimeKind.Utc), PaymentMethod.Card, "CARD-260113-005"),
                Payment(3, 3, 750m, new(2026, 6, 2, 14, 5, 0, DateTimeKind.Utc), PaymentMethod.Card, "CARD-260602-003"),
                Payment(4, 7, 85m, new(2026, 3, 20, 16, 40, 0, DateTimeKind.Utc), PaymentMethod.Card, "CARD-260320-007"),
                Payment(5, 8, 1000m, new(2026, 5, 8, 13, 10, 0, DateTimeKind.Utc), PaymentMethod.FinancialAid, "AID-260508-008A"),
                Payment(6, 8, 650m, new(2026, 5, 10, 19, 25, 0, DateTimeKind.Utc), PaymentMethod.BankTransfer, "BANK-260510-008B"),
                Payment(7, 6, 400m, new(2025, 8, 18, 12, 0, 0, DateTimeKind.Utc), PaymentMethod.FinancialAid, "AID-250818-006A"),
                Payment(8, 6, 280m, new(2025, 9, 2, 15, 45, 0, DateTimeKind.Utc), PaymentMethod.Card, "CARD-250902-006B"),
                Payment(9, 4, 2500m, new(2026, 1, 8, 17, 20, 0, DateTimeKind.Utc), PaymentMethod.FinancialAid, "AID-260108-004A"),
                Payment(10, 4, 2225m, new(2026, 1, 12, 15, 25, 0, DateTimeKind.Utc), PaymentMethod.BankTransfer, "BANK-260112-004B"));
        }

        if (!await database.Scholarships.AnyAsync())
        {
            database.Scholarships.AddRange(
                Scholarship("Future Engineers Merit Award", "Supports high-achieving engineering students with strong academic standing.", 3500m, 3.50m, "Engineering", 2, new(2026, 10, 15), true),
                Scholarship("Campus Leadership Scholarship", "Recognizes students who demonstrate sustained leadership and campus service.", 2000m, 3.00m, "Any", 2, new(2026, 11, 1), true),
                Scholarship("STEM Access Grant", "Need-aware award for students pursuing science, technology, engineering, or mathematics.", 2750m, 2.80m, "STEM", 1, new(2026, 12, 1), true),
                Scholarship("First-Generation Scholars Fund", "Assists first-generation university students who maintain satisfactory progress.", 3000m, 2.75m, "Any", 1, new(2026, 10, 30), true),
                Scholarship("Computing Innovation Award", "Rewards computer science students building practical community-focused technology.", 1800m, 3.25m, "Computer Science", 2, new(2026, 11, 15), true),
                Scholarship("Community Service Fellowship", "Supports students with documented volunteer and community engagement experience.", 1500m, 2.50m, "Any", 2, new(2026, 9, 30), true),
                Scholarship("Women in Engineering Scholarship", "Promotes the academic success of women enrolled in engineering programs.", 4000m, 3.20m, "Engineering", 1, new(2026, 12, 15), true),
                Scholarship("Sustainability Research Grant", "Funds student research addressing energy, climate, or sustainable infrastructure.", 2250m, 3.10m, "Engineering", 3, new(2027, 1, 20), true),
                Scholarship("Alumni Academic Excellence Award", "University alumni-funded recognition for exceptional academic achievement.", 5000m, 3.80m, "Any", 3, new(2026, 10, 10), true),
                Scholarship("Regional Student Opportunity Award", "Award for students from the region who demonstrate financial need.", 2500m, 2.70m, "Any", 1, new(2026, 8, 31), false));
        }

        await database.SaveChangesAsync();
    }

    private static Invoice Invoice(int id, string description, decimal amount, DateOnly issued, DateOnly due, InvoiceStatus status) => new()
    {
        Id = id,
        StudentId = 5,
        Description = description,
        Amount = amount,
        IssueDate = issued,
        DueDate = due,
        Status = status
    };

    private static Payment Payment(int id, int invoiceId, decimal amount, DateTime paidAtUtc, PaymentMethod method, string reference) => new()
    {
        Id = id,
        InvoiceId = invoiceId,
        StudentId = 5,
        Amount = amount,
        PaidAtUtc = paidAtUtc,
        Method = method,
        Reference = reference
    };

    private static Scholarship Scholarship(string name, string description, decimal amount, decimal gpa, string major, int year, DateOnly deadline, bool active) => new()
    {
        Name = name,
        Description = description,
        AwardAmount = amount,
        MinimumGpa = gpa,
        EligibleMajor = major,
        MinimumYearLevel = year,
        ApplicationDeadline = deadline,
        IsActive = active
    };
}

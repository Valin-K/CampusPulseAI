using FinancialAidBilling.Database.Data;
using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAidBilling.Tests;

public sealed class InvoiceTests
{
    [Test]
    public void Validate_ReturnsError_WhenDueDateIsBeforeIssueDate()
    {
        var request = new InvoiceRequest(4, "Tuition fees", 2500m, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 9), InvoiceStatus.Outstanding);

        var error = request.Validate();

        Assert.That(error, Is.EqualTo("Due date cannot be before issue date."));
    }

    [Test]
    public void Validate_ReturnsError_WhenAmountIsNotPositive()
    {
        var request = new InvoiceRequest(4, "Tuition fees", 0m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15), InvoiceStatus.Outstanding);

        var error = request.Validate();

        Assert.That(error, Is.EqualTo("Amount must be greater than zero."));
    }

    [Test]
    public async Task GetInvoicesAsync_ReturnsTenSeededRecordsForStudentFour()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);

        var invoices = await database.Invoices.AsNoTracking().Where(invoice => invoice.StudentId == 4).ToListAsync();

        Assert.That(invoices, Has.Count.EqualTo(10));
        Assert.That(invoices, Has.All.Property(nameof(Invoice.StudentId)).EqualTo(4));
    }

    [Test]
    public async Task GetPaymentsAsync_ReturnsTenSeededRecordsForStudentFour()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);

        var payments = await database.Payments.AsNoTracking().Where(payment => payment.StudentId == 4).ToListAsync();

        Assert.That(payments, Has.Count.EqualTo(10));
        Assert.That(payments, Has.All.Property(nameof(Payment.StudentId)).EqualTo(4));
    }

    [Test]
    public async Task GetScholarshipsAsync_ReturnsTenSeededRecords()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);

        var scholarships = await database.Scholarships.AsNoTracking().ToListAsync();

        Assert.That(scholarships, Has.Count.EqualTo(10));
        Assert.That(scholarships, Has.Some.Property(nameof(Scholarship.Name)).EqualTo("Future Engineers Merit Award"));
    }

    [Test]
    public async Task InitializeAsync_DoesNotDuplicateSeededRecords()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);
        await InvoiceSeedData.InitializeAsync(database);

        Assert.That(await database.Invoices.CountAsync(), Is.EqualTo(10));
        Assert.That(await database.Payments.CountAsync(), Is.EqualTo(10));
        Assert.That(await database.Scholarships.CountAsync(), Is.EqualTo(10));
    }

    private static FinancialAidBillingDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<FinancialAidBillingDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"financial-aid-billing-{Guid.NewGuid():N}.db")}")
            .Options;

        return new FinancialAidBillingDbContext(options);
    }
}

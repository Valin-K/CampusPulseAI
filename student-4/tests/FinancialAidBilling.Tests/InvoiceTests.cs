using FinancialAidBilling.Database.Data;
using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAidBilling.Tests;

public sealed class InvoiceTests
{
    [Test]
    public void Validate_ReturnsError_WhenDueDateIsBeforeIssueDate()
    {
        var request = new InvoiceRequest("S10001", "Tuition fees", 2500m, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 9), InvoiceStatus.Outstanding);

        var error = request.Validate();

        Assert.That(error, Is.EqualTo("Due date cannot be before issue date."));
    }

    [Test]
    public void Validate_ReturnsError_WhenAmountIsNotPositive()
    {
        var request = new InvoiceRequest("S10001", "Tuition fees", 0m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15), InvoiceStatus.Outstanding);

        var error = request.Validate();

        Assert.That(error, Is.EqualTo("Amount must be greater than zero."));
    }

    [Test]
    public async Task InitializeAsync_AddsSeedInvoicesOnlyOnce()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();

        await InvoiceSeedData.InitializeAsync(database);
        await InvoiceSeedData.InitializeAsync(database);

        Assert.That(await database.Invoices.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task UpdateFrom_UpdatesPersistedInvoiceFields()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        var invoice = new Invoice
        {
            StudentId = "S10001",
            Description = "Original fee",
            Amount = 100m,
            IssueDate = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 15),
            Status = InvoiceStatus.Outstanding
        };
        database.Invoices.Add(invoice);
        await database.SaveChangesAsync();

        invoice.UpdateFrom(new InvoiceRequest("S10002", "Updated tuition fee", 1250m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), InvoiceStatus.Paid));
        await database.SaveChangesAsync();

        var savedInvoice = await database.Invoices.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(savedInvoice.StudentId, Is.EqualTo("S10002"));
            Assert.That(savedInvoice.Description, Is.EqualTo("Updated tuition fee"));
            Assert.That(savedInvoice.Amount, Is.EqualTo(1250m));
            Assert.That(savedInvoice.Status, Is.EqualTo(InvoiceStatus.Paid));
        });
    }

    private static FinancialAidBillingDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<FinancialAidBillingDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"financial-aid-billing-{Guid.NewGuid():N}.db")}")
            .Options;

        return new FinancialAidBillingDbContext(options);
    }
}

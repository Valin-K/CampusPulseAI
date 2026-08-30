using FinancialAidBilling.Database.Data;
using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAidBilling.Tests;

public sealed class InvoiceTests
{
    [Fact]
    public void Validate_ReturnsError_WhenDueDateIsBeforeIssueDate()
    {
        var request = new InvoiceRequest(4, "Tuition fees", 2500m, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 9), InvoiceStatus.Outstanding);

        var error = request.Validate();

        Assert.Equal("Due date cannot be before issue date.", error);
    }

    [Fact]
    public void Validate_ReturnsError_WhenAmountIsNotPositive()
    {
        var request = new InvoiceRequest(4, "Tuition fees", 0m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15), InvoiceStatus.Outstanding);

        var error = request.Validate();

        Assert.Equal("Amount must be greater than zero.", error);
    }

    [Fact]
    public async Task GetInvoicesAsync_ReturnsTenSeededRecordsForStudentFour()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);

        var invoices = await database.Invoices.AsNoTracking().Where(invoice => invoice.StudentId == 4).ToListAsync();

        Assert.Equal(10, invoices.Count);
        Assert.All(invoices, invoice => Assert.Equal(4, invoice.StudentId));
    }

    [Fact]
    public async Task GetPaymentsAsync_ReturnsTenSeededRecordsForStudentFour()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);

        var payments = await database.Payments.AsNoTracking().Where(payment => payment.StudentId == 4).ToListAsync();

        Assert.Equal(10, payments.Count);
        Assert.All(payments, payment => Assert.Equal(4, payment.StudentId));
    }

    [Fact]
    public async Task GetScholarshipsAsync_ReturnsTenSeededRecords()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);

        var scholarships = await database.Scholarships.AsNoTracking().ToListAsync();

        Assert.Equal(10, scholarships.Count);
        Assert.Contains(scholarships, scholarship => scholarship.Name == "Future Engineers Merit Award");
    }

    [Fact]
    public async Task InitializeAsync_DoesNotDuplicateSeededRecords()
    {
        await using var database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await InvoiceSeedData.InitializeAsync(database);
        await InvoiceSeedData.InitializeAsync(database);

        Assert.Equal(10, await database.Invoices.CountAsync());
        Assert.Equal(10, await database.Payments.CountAsync());
        Assert.Equal(10, await database.Scholarships.CountAsync());
    }

    private static FinancialAidBillingDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<FinancialAidBillingDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"financial-aid-billing-{Guid.NewGuid():N}.db")}")
            .Options;

        return new FinancialAidBillingDbContext(options);
    }
}

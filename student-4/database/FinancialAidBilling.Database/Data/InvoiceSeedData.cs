using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAidBilling.Database.Data;

public static class InvoiceSeedData
{
    public static async Task InitializeAsync(FinancialAidBillingDbContext database)
    {
        if (await database.Invoices.AnyAsync())
        {
            return;
        }

        database.Invoices.AddRange(
            new Invoice
            {
                StudentId = "S10001",
                Description = "Semester 2 tuition fees",
                Amount = 4500.00m,
                IssueDate = new DateOnly(2026, 7, 1),
                DueDate = new DateOnly(2026, 8, 15),
                Status = InvoiceStatus.Outstanding
            },
            new Invoice
            {
                StudentId = "S10002",
                Description = "Laboratory and student services fees",
                Amount = 680.00m,
                IssueDate = new DateOnly(2026, 7, 1),
                DueDate = new DateOnly(2026, 8, 15),
                Status = InvoiceStatus.Paid
            });

        await database.SaveChangesAsync();
    }
}

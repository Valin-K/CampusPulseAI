using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAidBilling.Database.Data;

public sealed class FinancialAidBillingDbContext(DbContextOptions<FinancialAidBillingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("Invoices");
            entity.HasKey(invoice => invoice.Id);
            entity.Property(invoice => invoice.StudentId).HasMaxLength(32).IsRequired();
            entity.Property(invoice => invoice.Description).HasMaxLength(200).IsRequired();
            entity.Property(invoice => invoice.Amount).HasPrecision(12, 2);
            entity.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        });
    }
}

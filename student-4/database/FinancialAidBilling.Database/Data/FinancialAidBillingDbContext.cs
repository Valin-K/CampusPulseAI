using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialAidBilling.Database.Data;

public sealed class FinancialAidBillingDbContext(DbContextOptions<FinancialAidBillingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Scholarship> Scholarships => Set<Scholarship>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("Invoices");
            entity.HasKey(invoice => invoice.Id);
            entity.Property(invoice => invoice.Description).HasMaxLength(200).IsRequired();
            entity.Property(invoice => invoice.Amount).HasPrecision(12, 2);
            entity.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Amount).HasPrecision(12, 2);
            entity.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(payment => payment.Reference).HasMaxLength(64).IsRequired();
            entity.HasIndex(payment => payment.InvoiceId);
        });

        modelBuilder.Entity<Scholarship>(entity =>
        {
            entity.ToTable("Scholarships");
            entity.HasKey(scholarship => scholarship.Id);
            entity.Property(scholarship => scholarship.Name).HasMaxLength(120).IsRequired();
            entity.Property(scholarship => scholarship.Description).HasMaxLength(500).IsRequired();
            entity.Property(scholarship => scholarship.AwardAmount).HasPrecision(12, 2);
            entity.Property(scholarship => scholarship.MinimumGpa).HasPrecision(3, 2);
            entity.Property(scholarship => scholarship.EligibleMajor).HasMaxLength(100).IsRequired();
        });
    }
}

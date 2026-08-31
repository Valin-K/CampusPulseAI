using EventScheduling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Database.Data;

public sealed class EventSchedulingDbContext(DbContextOptions<EventSchedulingDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(entity =>
        {
            entity.ToTable("Rooms");
            entity.HasKey(room => room.Id);
            entity.Property(room => room.Name).HasMaxLength(120).IsRequired();
            entity.Property(room => room.Location).HasMaxLength(120).IsRequired();
            entity.Property(room => room.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.ToTable("Equipment");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings");
            entity.HasKey(booking => booking.Id);
            entity.Property(booking => booking.ResourceType).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(booking => booking.ResourceName).HasMaxLength(120).IsRequired();
            entity.Property(booking => booking.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(booking => booking.StudentId);
        });
    }
}

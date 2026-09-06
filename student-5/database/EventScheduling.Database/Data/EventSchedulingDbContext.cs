using EventScheduling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Database.Data;

public sealed class EventSchedulingDbContext(DbContextOptions<EventSchedulingDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RefEquipment> RefEquipment => Set<RefEquipment>();
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
            entity.Property(room => room.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(room => room.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(room => room.OpeningHours).HasMaxLength(100).HasDefaultValue(string.Empty).IsRequired();
            entity.Property(room => room.IsAccessible).HasDefaultValue(false).IsRequired();
        });

        modelBuilder.Entity<RefEquipment>(entity =>
        {
            entity.ToTable("RefEquipment");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.ToTable("Equipment");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.SerialNumber).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(8).IsRequired();
            entity.HasOne<RefEquipment>()
                .WithMany()
                .HasForeignKey(item => item.RefEquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.RefEquipmentId, item.Status });
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings");
            entity.HasKey(booking => booking.Id);
            entity.Property(booking => booking.ResourceName).HasMaxLength(120).IsRequired();
            entity.Property(booking => booking.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(booking => booking.CheckOutConditionNotes).HasMaxLength(Booking.MaxConditionNotesLength);
            entity.Property(booking => booking.ReturnConditionNotes).HasMaxLength(Booking.MaxConditionNotesLength);
            entity.HasIndex(booking => booking.StudentId);
            entity.HasOne<Room>()
                .WithMany()
                .HasForeignKey(booking => booking.RoomId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Equipment>()
                .WithMany()
                .HasForeignKey(booking => booking.EquipmentId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<RefEquipment>()
                .WithMany()
                .HasForeignKey(booking => booking.RefEquipmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

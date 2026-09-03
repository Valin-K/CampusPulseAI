using CampusHousing.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusHousing.Database.Data;

public sealed class CampusHousingDbContext(DbContextOptions<CampusHousingDbContext> options) : DbContext(options)
{
    public DbSet<DormRoom> DormRooms => Set<DormRoom>();
    public DbSet<HousingAllocation> HousingAllocations => Set<HousingAllocation>();
    public DbSet<MaintenanceTicket> MaintenanceTickets => Set<MaintenanceTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DormRoom>().HasIndex(room => new { room.Building, room.RoomNumber }).IsUnique();
        modelBuilder.Entity<HousingAllocation>().HasIndex(allocation => allocation.StudentId).IsUnique();
        modelBuilder.Entity<DormRoom>().HasData(
            new DormRoom { Id = 1, Building = "Maple Hall", RoomNumber = "101", Capacity = 2, IsAvailable = true },
            new DormRoom { Id = 2, Building = "Maple Hall", RoomNumber = "102", Capacity = 2, IsAvailable = true },
            new DormRoom { Id = 3, Building = "Oak Hall", RoomNumber = "201", Capacity = 4, IsAvailable = true },
            new DormRoom { Id = 4, Building = "Oak Hall", RoomNumber = "202", Capacity = 4, IsAvailable = true },
            new DormRoom { Id = 5, Building = "Cedar Hall", RoomNumber = "301", Capacity = 2, IsAvailable = true },
            new DormRoom { Id = 6, Building = "Cedar Hall", RoomNumber = "302", Capacity = 2, IsAvailable = true },
            new DormRoom { Id = 7, Building = "Pine Hall", RoomNumber = "401", Capacity = 1, IsAvailable = true },
            new DormRoom { Id = 8, Building = "Pine Hall", RoomNumber = "402", Capacity = 1, IsAvailable = true },
            new DormRoom { Id = 9, Building = "Birch Hall", RoomNumber = "501", Capacity = 4, IsAvailable = true },
            new DormRoom { Id = 10, Building = "Birch Hall", RoomNumber = "502", Capacity = 4, IsAvailable = true });
        modelBuilder.Entity<HousingAllocation>().HasData(
            new HousingAllocation { Id = 1, DormRoomId = 1, StudentId = "STU-000003", StudentName = "Seed placeholder", RoommatePreference = "Quiet", AllocatedAt = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc) });
    }
}

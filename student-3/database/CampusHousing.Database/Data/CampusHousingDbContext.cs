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
            new DormRoom { Id = 1, Building = "Maple Hall", RoomNumber = "101", Capacity = 2 },
            new DormRoom { Id = 2, Building = "Maple Hall", RoomNumber = "102", Capacity = 2 },
            new DormRoom { Id = 3, Building = "Oak Hall", RoomNumber = "201", Capacity = 4 },
            new DormRoom { Id = 4, Building = "Oak Hall", RoomNumber = "202", Capacity = 4 },
            new DormRoom { Id = 5, Building = "Cedar Hall", RoomNumber = "301", Capacity = 2 },
            new DormRoom { Id = 6, Building = "Cedar Hall", RoomNumber = "302", Capacity = 2 },
            new DormRoom { Id = 7, Building = "Pine Hall", RoomNumber = "401", Capacity = 1 },
            new DormRoom { Id = 8, Building = "Pine Hall", RoomNumber = "402", Capacity = 1 },
            new DormRoom { Id = 9, Building = "Birch Hall", RoomNumber = "501", Capacity = 4 },
            new DormRoom { Id = 10, Building = "Birch Hall", RoomNumber = "502", Capacity = 4 });

        modelBuilder.Entity<HousingAllocation>().HasData(
            new HousingAllocation { Id = 1, DormRoomId = 1, StudentId = "STU-000003", StudentName = "Jack Wang", RoommatePreference = "Quiet", AllocatedAt = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 2, DormRoomId = 1, StudentId = "STU-202502", StudentName = "Maya Patel", RoommatePreference = "Social", AllocatedAt = new DateTime(2025, 1, 11, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 3, DormRoomId = 2, StudentId = "STU-202503", StudentName = "Liam Chen", RoommatePreference = "Quiet", AllocatedAt = new DateTime(2025, 1, 12, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 4, DormRoomId = 3, StudentId = "STU-202504", StudentName = "Sofia Garcia", RoommatePreference = "Any", AllocatedAt = new DateTime(2025, 1, 13, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 5, DormRoomId = 3, StudentId = "STU-202505", StudentName = "Noah Brown", RoommatePreference = "Quiet", AllocatedAt = new DateTime(2025, 1, 14, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 6, DormRoomId = 4, StudentId = "STU-202506", StudentName = "Ava Wilson", RoommatePreference = "Social", AllocatedAt = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 7, DormRoomId = 5, StudentId = "STU-202507", StudentName = "Ethan Davis", RoommatePreference = "Any", AllocatedAt = new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 8, DormRoomId = 6, StudentId = "STU-202508", StudentName = "Emma Miller", RoommatePreference = "Quiet", AllocatedAt = new DateTime(2025, 1, 17, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 9, DormRoomId = 7, StudentId = "STU-202509", StudentName = "Oliver Moore", RoommatePreference = "Any", AllocatedAt = new DateTime(2025, 1, 18, 0, 0, 0, DateTimeKind.Utc) },
            new HousingAllocation { Id = 10, DormRoomId = 8, StudentId = "STU-202510", StudentName = "Isabella Taylor", RoommatePreference = "Quiet", AllocatedAt = new DateTime(2025, 1, 19, 0, 0, 0, DateTimeKind.Utc) });

        modelBuilder.Entity<MaintenanceTicket>().HasData(
            new MaintenanceTicket { Id = 1, StudentId = "STU-000003", DormRoomId = 1, Description = "Bedroom light is flickering", Severity = "Low", RepairQueue = "Electrical", Status = "Open", CreatedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 2, StudentId = "STU-202502", DormRoomId = 1, Description = "No hot water in the bathroom", Severity = "High", RepairQueue = "Plumbing", Status = "In Progress", CreatedAt = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 3, StudentId = "STU-202503", DormRoomId = 2, Description = "Heater is not working", Severity = "High", RepairQueue = "Heating", Status = "Open", CreatedAt = new DateTime(2025, 2, 3, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 4, StudentId = "STU-202504", DormRoomId = 3, Description = "Leaking sink", Severity = "Medium", RepairQueue = "Plumbing", Status = "Resolved", CreatedAt = new DateTime(2025, 2, 4, 0, 0, 0, DateTimeKind.Utc), ResolvedAt = new DateTime(2025, 2, 5, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 5, StudentId = "STU-202505", DormRoomId = 3, Description = "Door lock is sticking", Severity = "Medium", RepairQueue = "Security", Status = "Open", CreatedAt = new DateTime(2025, 2, 5, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 6, StudentId = "STU-202506", DormRoomId = 4, Description = "Smoke detector chirping", Severity = "High", RepairQueue = "Security", Status = "In Progress", CreatedAt = new DateTime(2025, 2, 6, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 7, StudentId = "STU-202507", DormRoomId = 5, Description = "Window will not close", Severity = "Low", RepairQueue = "General Maintenance", Status = "Open", CreatedAt = new DateTime(2025, 2, 7, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 8, StudentId = "STU-202508", DormRoomId = 6, Description = "Power outlet is loose", Severity = "Medium", RepairQueue = "Electrical", Status = "Open", CreatedAt = new DateTime(2025, 2, 8, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 9, StudentId = "STU-202509", DormRoomId = 7, Description = "Ceiling leak after rain", Severity = "High", RepairQueue = "Plumbing", Status = "In Progress", CreatedAt = new DateTime(2025, 2, 9, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceTicket { Id = 10, StudentId = "STU-202510", DormRoomId = 8, Description = "Common area light is out", Severity = "Low", RepairQueue = "Electrical", Status = "Resolved", CreatedAt = new DateTime(2025, 2, 10, 0, 0, 0, DateTimeKind.Utc), ResolvedAt = new DateTime(2025, 2, 11, 0, 0, 0, DateTimeKind.Utc) });
    }
}

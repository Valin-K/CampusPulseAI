using CampusHousing.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusHousing.Database.Data;

public static class CampusHousingSeedData
{
    public static async Task EnsureSeededAsync(CampusHousingDbContext db)
    {
        if (!await db.DormRooms.AnyAsync())
        {
            db.DormRooms.AddRange(
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
            await db.SaveChangesAsync();
        }

        if (!await db.HousingAllocations.AnyAsync())
        {
            var students = new[] { "Jack Wang", "Maya Patel", "Liam Chen", "Sofia Garcia", "Noah Brown", "Ava Wilson", "Ethan Davis", "Emma Miller", "Oliver Moore", "Isabella Taylor" };
            for (var i = 0; i < students.Length; i++)
            {
                db.HousingAllocations.Add(new HousingAllocation
                {
                    Id = i + 1,
                    DormRoomId = i switch { 0 or 1 => 1, 2 => 2, 3 or 4 => 3, 5 => 4, 6 => 5, 7 => 6, 8 => 7, _ => 8 },
                    StudentId = $"STU-{000003 + i}",
                    StudentName = students[i],
                    RoommatePreference = i % 3 == 0 ? "Quiet" : i % 3 == 1 ? "Social" : "Any",
                    AllocatedAt = new DateTime(2025, 1, 10 + i, 0, 0, 0, DateTimeKind.Utc)
                });
            }
            await db.SaveChangesAsync();
        }

        if (!await db.MaintenanceTickets.AnyAsync())
        {
            var descriptions = new[] { "Bedroom light is flickering", "No hot water in the bathroom", "Heater is not working", "Leaking sink", "Door lock is sticking", "Smoke detector chirping", "Window will not close", "Power outlet is loose", "Ceiling leak after rain", "Common area light is out" };
            for (var i = 0; i < descriptions.Length; i++)
            {
                db.MaintenanceTickets.Add(new MaintenanceTicket
                {
                    Id = i + 1,
                    StudentId = $"STU-{000003 + i}",
                    DormRoomId = i < 8 ? i switch { 0 or 1 => 1, 2 => 2, 3 or 4 => 3, 5 => 4, 6 => 5, _ => 6 } : null,
                    Description = descriptions[i],
                    Severity = i is 1 or 2 or 5 or 8 ? "High" : i is 3 or 4 or 7 ? "Medium" : "Low",
                    RepairQueue = i is 1 or 3 ? "Plumbing" : i is 2 ? "Heating" : i is 4 or 5 ? "Security" : i is 0 or 7 or 9 ? "Electrical" : "General Maintenance",
                    Status = i is 3 or 9 ? "Resolved" : i is 1 or 5 or 8 ? "In Progress" : "Open",
                    CreatedAt = new DateTime(2025, 2, 1 + i, 0, 0, 0, DateTimeKind.Utc),
                    ResolvedAt = i is 3 or 9 ? new DateTime(2025, 2, 2 + i, 0, 0, 0, DateTimeKind.Utc) : null
                });
            }
            await db.SaveChangesAsync();
        }
    }
}
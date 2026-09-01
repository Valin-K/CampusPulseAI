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
        modelBuilder.Entity<DormRoom>().HasData(
            new DormRoom { Id = 1, Building = "Maple Hall", RoomNumber = "101", Capacity = 2 },
            new DormRoom { Id = 2, Building = "Maple Hall", RoomNumber = "102", Capacity = 2 },
            new DormRoom { Id = 3, Building = "Oak Hall", RoomNumber = "201", Capacity = 4 });
    }
}

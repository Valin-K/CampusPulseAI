using CampusHousing.Database.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CampusHousing.Tests;

public sealed class CampusHousingDatabaseTests
{
    [Test]
    public async Task EnsureSeededAsync_PopulatesExpectedDataAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.DormRooms.RemoveRange(db.DormRooms);
        db.HousingAllocations.RemoveRange(db.HousingAllocations);
        db.MaintenanceTickets.RemoveRange(db.MaintenanceTickets);
        await db.SaveChangesAsync();

        await CampusHousingSeedData.EnsureSeededAsync(db);
        db.HousingAllocations.Single(allocation => allocation.Id == 1).StudentName = "Changed Name";
        await db.SaveChangesAsync();
        await CampusHousingSeedData.EnsureSeededAsync(db);

        Assert.Multiple(() =>
        {
            Assert.That(db.DormRooms.Count(), Is.EqualTo(10));
            Assert.That(db.HousingAllocations.Count(), Is.EqualTo(10));
            Assert.That(db.HousingAllocations.Single(allocation => allocation.Id == 1).StudentName, Is.EqualTo("Jack Wang"));
            Assert.That(db.MaintenanceTickets.Count(), Is.EqualTo(10));
            Assert.That(db.DormRooms.Single(room => room.Id == 1).Building, Is.EqualTo("Maple Hall"));
            Assert.That(db.MaintenanceTickets.Single(ticket => ticket.Id == 1).RepairQueue, Is.EqualTo("Electrical"));
        });
    }

    [Test]
    public async Task DatabaseRejectsDuplicateRoomIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.DormRooms.Add(new() { Building = "Maple Hall", RoomNumber = "101", Capacity = 2 });

        Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    [Test]
    public async Task DatabaseRejectsDuplicateStudentAllocation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.HousingAllocations.Add(new()
        {
            DormRoomId = 1,
            StudentId = "STU-000003",
            StudentName = "Another Name"
        });

        Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    [Test]
    public void ModelConfiguresUniqueIndexes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = CreateContext(connection);
        var roomIndexes = db.Model.FindEntityType(typeof(CampusHousing.Database.Models.DormRoom))!.GetIndexes();
        var allocationIndexes = db.Model.FindEntityType(typeof(CampusHousing.Database.Models.HousingAllocation))!.GetIndexes();

        Assert.Multiple(() =>
        {
            Assert.That(roomIndexes.Any(index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(new[] { "Building", "RoomNumber" })), Is.True);
            Assert.That(allocationIndexes.Any(index => index.IsUnique && index.Properties.Single().Name == "StudentId"), Is.True);
        });
    }

    private static CampusHousingDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<CampusHousingDbContext>().UseSqlite(connection).Options);
}

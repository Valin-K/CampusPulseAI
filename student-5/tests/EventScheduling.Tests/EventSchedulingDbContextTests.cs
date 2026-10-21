using EventScheduling.Database.Data;
using EventScheduling.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Tests;

public sealed class EventSchedulingDbContextTests
{
    [Test]
    public void ReferenceEquipmentReadsAreSharedAndMutationsAreStaffOnly()
    {
        var source = File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "database", "EventScheduling.Database", "Program.cs"));
        var start = source.IndexOf("var refEquipment = app.MapGroup(\"/api/refequipment\")", StringComparison.Ordinal);
        var end = source.IndexOf("refEquipment.MapPost(\"/\"", start, StringComparison.Ordinal);
        var readRoutes = source[start..end];

        var mutationRoutes = source[end..source.IndexOf("var bookings", end, StringComparison.Ordinal)];
        Assert.Multiple(() =>
        {
            Assert.That(readRoutes, Does.Not.Contain("RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy)"));
            Assert.That(mutationRoutes, Does.Contain("RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy)"));
        });
    }

    private static RefEquipment RefEquipmentType(string name = "HDMI Adapter") =>
        new() { Name = name, Category = "Cables & Adapters", Description = "Test type." };

    private static Equipment Unit(int refEquipmentId, string serial = "HDMI-001",
        EquipmentStatus status = EquipmentStatus.AVL) =>
        new() { RefEquipmentId = refEquipmentId, SerialNumber = serial, Status = status };

    [Test]
    public async Task RoomKindIsStoredAsAReadableString()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        context.Database.Rooms.Add(new Room
        {
            Name = "Physics Lab",
            Location = "Science Building",
            Capacity = 20,
            Description = "Bench lab.",
            Kind = RoomKind.Lab
        });
        await context.Database.SaveChangesAsync();

        var stored = await context.Database.Database
            .SqlQuery<string>($"SELECT Kind AS Value FROM Rooms")
            .SingleAsync();

        Assert.That(stored, Is.EqualTo("Lab"));
    }

    [Test]
    public async Task EquipmentStatusIsStoredAsAReadableString()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var type = RefEquipmentType();
        context.Database.RefEquipment.Add(type);
        await context.Database.SaveChangesAsync();
        context.Database.Equipment.Add(Unit(type.Id, status: EquipmentStatus.OCC));
        await context.Database.SaveChangesAsync();

        var stored = await context.Database.Database
            .SqlQuery<string>($"SELECT Status AS Value FROM Equipment")
            .SingleAsync();

        Assert.That(stored, Is.EqualTo("OCC"));
    }

    [Test]
    public async Task AnEquipmentTypeCannotBeDeletedWhileUnitsReferenceIt()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var type = RefEquipmentType();
        context.Database.RefEquipment.Add(type);
        await context.Database.SaveChangesAsync();
        context.Database.Equipment.Add(Unit(type.Id));
        await context.Database.SaveChangesAsync();
        context.Database.ChangeTracker.Clear();

        context.Database.RefEquipment.Remove(await context.Database.RefEquipment.SingleAsync());

        Assert.ThrowsAsync<DbUpdateException>(() => context.Database.SaveChangesAsync());
    }

    [Test]
    public async Task DeletingARoomDetachesItsBookingsInsteadOfDeletingThem()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var room = new Room { Name = "Study Room 101", Location = "Library", Capacity = 4, Description = "x" };
        context.Database.Rooms.Add(room);
        await context.Database.SaveChangesAsync();
        context.Database.Bookings.Add(new Booking
        {
            StudentId = 4,
            RoomId = room.Id,
            ResourceName = "Study Room 101",
            StartUtc = new DateTime(2031, 3, 1, 10, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2031, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            Status = BookingStatus.Confirmed
        });
        await context.Database.SaveChangesAsync();
        context.Database.ChangeTracker.Clear();

        context.Database.Rooms.Remove(await context.Database.Rooms.SingleAsync());
        await context.Database.SaveChangesAsync();

        var booking = await context.Database.Bookings.SingleAsync();
        Assert.That(booking.RoomId, Is.Null);
    }

    [Test]
    public async Task DeletingAnEquipmentUnitDetachesTheBookingThatHeldIt()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var type = RefEquipmentType();
        context.Database.RefEquipment.Add(type);
        await context.Database.SaveChangesAsync();
        var unit = Unit(type.Id, status: EquipmentStatus.OCC);
        context.Database.Equipment.Add(unit);
        await context.Database.SaveChangesAsync();
        context.Database.Bookings.Add(new Booking
        {
            StudentId = 4,
            RefEquipmentId = type.Id,
            EquipmentId = unit.Id,
            ResourceName = "HDMI Adapter",
            StartUtc = new DateTime(2031, 3, 1, 10, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2031, 3, 3, 10, 0, 0, DateTimeKind.Utc),
            Status = BookingStatus.Confirmed
        });
        await context.Database.SaveChangesAsync();
        context.Database.ChangeTracker.Clear();

        context.Database.Equipment.Remove(await context.Database.Equipment.SingleAsync());
        await context.Database.SaveChangesAsync();

        var booking = await context.Database.Bookings.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(booking.EquipmentId, Is.Null);
            Assert.That(booking.RefEquipmentId, Is.EqualTo(type.Id), "the equipment type is kept for history");
        });
    }

    sealed class DatabaseTestContext(
        SqliteConnection connection,
        EventSchedulingDbContext database) : IAsyncDisposable
    {
        public EventSchedulingDbContext Database { get; } = database;

        public static async Task<DatabaseTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<EventSchedulingDbContext>()
                .UseSqlite(connection)
                .Options;
            var database = new EventSchedulingDbContext(options);
            await database.Database.EnsureCreatedAsync();

            return new DatabaseTestContext(connection, database);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

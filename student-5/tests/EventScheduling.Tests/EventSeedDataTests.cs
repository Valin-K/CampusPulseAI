using EventScheduling.Database.Data;
using EventScheduling.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Tests;
public sealed class EventSeedDataTests
{
    [Test]
    public async Task SeedsTheRoomCatalogueSplitByKind()
    {
        await using var context = await DatabaseTestContext.CreateSeededAsync();
        var rooms = await context.Database.Rooms.ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rooms, Has.Count.EqualTo(15));
            Assert.That(rooms.Count(room => room.Kind == RoomKind.Study), Is.EqualTo(5));
            Assert.That(rooms.Count(room => room.Kind == RoomKind.Lab), Is.EqualTo(4));
            Assert.That(rooms.Count(room => room.Kind == RoomKind.Lecture), Is.EqualTo(3));
            Assert.That(rooms.Count(room => room.Kind == RoomKind.Collaborative), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task SeedsTheEquipmentCatalogueAndUnitLedger()
    {
        await using var context = await DatabaseTestContext.CreateSeededAsync();
        var units = await context.Database.Equipment.ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(context.Database.RefEquipment.Count(), Is.EqualTo(11));
            Assert.That(units, Has.Count.EqualTo(70));
            Assert.That(units.Count(unit => unit.Status == EquipmentStatus.OCC), Is.EqualTo(9));
            Assert.That(units.Count(unit => unit.Status == EquipmentStatus.AVL), Is.EqualTo(61));
        });
    }

    [Test]
    public async Task SeedsBookingsForTheStudentAndTheTeacher()
    {
        await using var context = await DatabaseTestContext.CreateSeededAsync();
        var bookings = await context.Database.Bookings.ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(bookings, Has.Count.EqualTo(51));
            Assert.That(bookings.Count(booking => booking.StudentId == 4), Is.EqualTo(19));
            Assert.That(bookings.Count(booking => booking.StudentId == 14), Is.EqualTo(32));
        });
    }

    [Test]
    public async Task EverySeededBookingHoldsExactlyOneResource()
    {
        await using var context = await DatabaseTestContext.CreateSeededAsync();
        var bookings = await context.Database.Bookings.ToListAsync();

        Assert.That(bookings.All(booking => booking.RoomId.HasValue ^ booking.RefEquipmentId.HasValue), Is.True);
    }

    [Test]
    public async Task NoSeededBookingCarriesTheDerivedOverdueStatus()
    {
        await using var context = await DatabaseTestContext.CreateSeededAsync();

        Assert.That(await context.Database.Bookings.AnyAsync(b => b.Status == BookingStatus.Overdue), Is.False);
    }

    [Test]
    public async Task EveryOccupiedUnitIsHeldByOneBooking()
    {
        await using var context = await DatabaseTestContext.CreateSeededAsync();

        var occupiedUnitIds = await context.Database.Equipment
            .Where(unit => unit.Status == EquipmentStatus.OCC)
            .Select(unit => unit.Id)
            .ToListAsync();
        var heldUnitIds = await context.Database.Bookings
            .Where(booking => booking.EquipmentId != null)
            .Select(booking => booking.EquipmentId!.Value)
            .ToListAsync();

        Assert.That(occupiedUnitIds, Is.EquivalentTo(heldUnitIds));
    }

    [Test]
    public async Task RunningTheSeederASecondTimeChangesNothing()
    {
        await using var context = await DatabaseTestContext.CreateSeededAsync();

        await EventSeedData.InitializeAsync(context.Database);

        Assert.Multiple(() =>
        {
            Assert.That(context.Database.Rooms.Count(), Is.EqualTo(15));
            Assert.That(context.Database.Equipment.Count(), Is.EqualTo(70));
            Assert.That(context.Database.Bookings.Count(), Is.EqualTo(51));
        });
    }

    sealed class DatabaseTestContext(
        SqliteConnection connection,
        EventSchedulingDbContext database) : IAsyncDisposable
    {
        public EventSchedulingDbContext Database { get; } = database;

        public static async Task<DatabaseTestContext> CreateSeededAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<EventSchedulingDbContext>()
                .UseSqlite(connection)
                .Options;
            var database = new EventSchedulingDbContext(options);
            await database.Database.EnsureCreatedAsync();
            await EventSeedData.InitializeAsync(database);

            return new DatabaseTestContext(connection, database);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

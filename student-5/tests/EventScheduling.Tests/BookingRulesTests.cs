using EventScheduling.Database.Data;
using EventScheduling.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Tests;

public sealed class BookingRulesTests
{
    private static readonly DateTime NowUtc = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public void HasSufficientNotice_RejectsAStartTimeSoonerThanTheMinimumNotice()
    {
        var start = NowUtc.Add(Booking.MinBookingNotice).AddMinutes(-1);

        Assert.That(BookingRules.HasSufficientNotice(start, NowUtc), Is.False);
    }

    [Test]
    public void HasSufficientNotice_AllowsAStartTimeExactlyAtTheMinimumNotice()
    {
        var start = NowUtc.Add(Booking.MinBookingNotice);

        Assert.That(BookingRules.HasSufficientNotice(start, NowUtc), Is.True);
    }

    [Test]
    public void HasSufficientNotice_AllowsAStartTimeWellInTheFuture()
    {
        Assert.That(BookingRules.HasSufficientNotice(NowUtc.AddDays(1), NowUtc), Is.True);
    }

    [Test]
    public void HasCapacity_AllowsAnAttendeeCountAtOrBelowRoomCapacity()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BookingRules.HasCapacity(4, roomCapacity: 4), Is.True);
            Assert.That(BookingRules.HasCapacity(3, roomCapacity: 4), Is.True);
        });
    }

    [Test]
    public void HasCapacity_RejectsAnAttendeeCountOverRoomCapacity()
    {
        Assert.That(BookingRules.HasCapacity(5, roomCapacity: 4), Is.False);
    }

    [Test]
    public void HasCapacity_SkipsTheCheckWhenNoAttendeeCountIsGiven()
    {
        Assert.That(BookingRules.HasCapacity(null, roomCapacity: 1), Is.True);
    }

    [Test]
    public void IsBeforeCutoff_AllowsChangingABookingThatHasNotStartedYet()
    {
        Assert.That(BookingRules.IsBeforeCutoff(NowUtc.AddMinutes(1), NowUtc), Is.True);
    }

    [Test]
    public void IsBeforeCutoff_RejectsABookingThatHasAlreadyStarted()
    {
        Assert.That(BookingRules.IsBeforeCutoff(NowUtc, NowUtc), Is.False);
    }

    [Test]
    public void IsBeforeCutoff_RejectsABookingThatHasAlreadyEnded()
    {
        Assert.That(BookingRules.IsBeforeCutoff(NowUtc.AddHours(-2), NowUtc), Is.False);
    }

    [Test]
    public async Task CountActiveBookingsAsync_CountsOnlyPendingOrConfirmedBookingsThatHaveNotYetEnded()
    {
        await using var context = await DatabaseTestContext.CreateAsync();

        var room = new Room { Name = "Study Room 101", Location = "Library", Capacity = 4, Description = "x" };
        context.Database.Rooms.Add(room);
        await context.Database.SaveChangesAsync();

        context.Database.Bookings.AddRange(
            MakeBooking(room.Id, studentId: 4, status: BookingStatus.Confirmed, endUtc: NowUtc.AddDays(1)),
            MakeBooking(room.Id, studentId: 4, status: BookingStatus.Pending, endUtc: NowUtc.AddHours(2)),
            MakeBooking(room.Id, studentId: 4, status: BookingStatus.Cancelled, endUtc: NowUtc.AddDays(1)),
            MakeBooking(room.Id, studentId: 4, status: BookingStatus.Completed, endUtc: NowUtc.AddDays(1)),
            MakeBooking(room.Id, studentId: 4, status: BookingStatus.Returned, endUtc: NowUtc.AddDays(1)),
            MakeBooking(room.Id, studentId: 4, status: BookingStatus.Confirmed, endUtc: NowUtc.AddHours(-1)),
            MakeBooking(room.Id, studentId: 14, status: BookingStatus.Confirmed, endUtc: NowUtc.AddDays(1)));
        await context.Database.SaveChangesAsync();

        var count = await BookingRules.CountActiveBookingsAsync(context.Database, studentId: 4, NowUtc, CancellationToken.None);

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task CountActiveBookingsAsync_ReturnsZeroForAStudentWithNoBookings()
    {
        await using var context = await DatabaseTestContext.CreateAsync();

        var count = await BookingRules.CountActiveBookingsAsync(context.Database, studentId: 4, NowUtc, CancellationToken.None);

        Assert.That(count, Is.Zero);
    }

    private static Booking MakeBooking(int roomId, int studentId, BookingStatus status, DateTime endUtc) => new()
    {
        StudentId = studentId,
        RoomId = roomId,
        ResourceName = "Study Room 101",
        StartUtc = endUtc.AddHours(-1),
        EndUtc = endUtc,
        Status = status
    };


    [Test]
    public async Task HasRoomClashAsync_DoesNotClashWhenTheExistingBookingEndsExactlyWhenTheNewOneStarts()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var room = await SeedRoomAsync(context);
        var fourteenHundred = new DateTime(2030, 6, 1, 14, 0, 0, DateTimeKind.Utc);

        await SeedRoomBookingAsync(context, room.Id, fourteenHundred.AddHours(-1), fourteenHundred);

        var clashes = await BookingRules.HasRoomClashAsync(
            context.Database, room.Id, fourteenHundred, fourteenHundred.AddHours(1), excludeBookingId: null, CancellationToken.None);

        Assert.That(clashes, Is.False);
    }

    [Test]
    public async Task HasRoomClashAsync_DoesNotClashWhenTheNewBookingEndsExactlyWhenTheExistingOneStarts()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var room = await SeedRoomAsync(context);
        var fourteenHundred = new DateTime(2030, 6, 1, 14, 0, 0, DateTimeKind.Utc);

        await SeedRoomBookingAsync(context, room.Id, fourteenHundred, fourteenHundred.AddHours(1));

        var clashes = await BookingRules.HasRoomClashAsync(
            context.Database, room.Id, fourteenHundred.AddHours(-1), fourteenHundred, excludeBookingId: null, CancellationToken.None);

        Assert.That(clashes, Is.False);
    }

    [Test]
    public async Task HasRoomClashAsync_ClashesWhenTheWindowsOverlapByEvenAMinute()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var room = await SeedRoomAsync(context);
        var fourteenHundred = new DateTime(2030, 6, 1, 14, 0, 0, DateTimeKind.Utc);

        await SeedRoomBookingAsync(context, room.Id, fourteenHundred.AddHours(-1), fourteenHundred.AddMinutes(1));

        var clashes = await BookingRules.HasRoomClashAsync(
            context.Database, room.Id, fourteenHundred, fourteenHundred.AddHours(1), excludeBookingId: null, CancellationToken.None);

        Assert.That(clashes, Is.True);
    }

    private static async Task<Room> SeedRoomAsync(DatabaseTestContext context)
    {
        var room = new Room { Name = "Study Room 101", Location = "Library", Capacity = 4, Description = "x" };
        context.Database.Rooms.Add(room);
        await context.Database.SaveChangesAsync();
        return room;
    }

    private static async Task SeedRoomBookingAsync(DatabaseTestContext context, int roomId, DateTime startUtc, DateTime endUtc)
    {
        context.Database.Bookings.Add(new Booking
        {
            StudentId = 4,
            RoomId = roomId,
            ResourceName = "Study Room 101",
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = BookingStatus.Confirmed
        });
        await context.Database.SaveChangesAsync();
    }

    [Test]
    public async Task CountAvailableEquipmentUnitsAsync_ExcludesAUnitBookedForAnOverlappingWindow()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var type = new RefEquipment { Name = "Portable Projector", Category = "Presentation", Description = "x" };
        context.Database.RefEquipment.Add(type);
        await context.Database.SaveChangesAsync();

        var unit1 = new Equipment { RefEquipmentId = type.Id, SerialNumber = "PRJ-001", Status = EquipmentStatus.AVL };
        var unit2 = new Equipment { RefEquipmentId = type.Id, SerialNumber = "PRJ-002", Status = EquipmentStatus.AVL };
        context.Database.Equipment.AddRange(unit1, unit2);
        await context.Database.SaveChangesAsync();

        context.Database.Bookings.Add(new Booking
        {
            StudentId = 4,
            RefEquipmentId = type.Id,
            EquipmentId = unit1.Id,
            ResourceName = "Portable Projector",
            StartUtc = NowUtc,
            EndUtc = NowUtc.AddDays(3),
            Status = BookingStatus.Confirmed
        });
        await context.Database.SaveChangesAsync();

        var count = await BookingRules.CountAvailableEquipmentUnitsAsync(
            context.Database, type.Id, NowUtc.AddDays(1), NowUtc.AddDays(2), CancellationToken.None);

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task CountAvailableEquipmentUnitsAsync_IgnoresACancelledBookingOnTheOnlyUnit()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var type = new RefEquipment { Name = "Portable Projector", Category = "Presentation", Description = "x" };
        context.Database.RefEquipment.Add(type);
        await context.Database.SaveChangesAsync();

        var unit = new Equipment { RefEquipmentId = type.Id, SerialNumber = "PRJ-001", Status = EquipmentStatus.AVL };
        context.Database.Equipment.Add(unit);
        await context.Database.SaveChangesAsync();

        context.Database.Bookings.Add(new Booking
        {
            StudentId = 4,
            RefEquipmentId = type.Id,
            EquipmentId = unit.Id,
            ResourceName = "Portable Projector",
            StartUtc = NowUtc,
            EndUtc = NowUtc.AddDays(3),
            Status = BookingStatus.Cancelled
        });
        await context.Database.SaveChangesAsync();

        var count = await BookingRules.CountAvailableEquipmentUnitsAsync(
            context.Database, type.Id, NowUtc.AddDays(1), NowUtc.AddDays(2), CancellationToken.None);

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task CountAvailableEquipmentUnitsAsync_ExcludesUnitsInMaintenanceOrRetired()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var type = new RefEquipment { Name = "Portable Projector", Category = "Presentation", Description = "x" };
        context.Database.RefEquipment.Add(type);
        await context.Database.SaveChangesAsync();

        context.Database.Equipment.AddRange(
            new Equipment { RefEquipmentId = type.Id, SerialNumber = "PRJ-001", Status = EquipmentStatus.AVL },
            new Equipment { RefEquipmentId = type.Id, SerialNumber = "PRJ-002", Status = EquipmentStatus.MTC },
            new Equipment { RefEquipmentId = type.Id, SerialNumber = "PRJ-003", Status = EquipmentStatus.RET });
        await context.Database.SaveChangesAsync();

        var count = await BookingRules.CountAvailableEquipmentUnitsAsync(
            context.Database, type.Id, NowUtc, NowUtc.AddDays(1), CancellationToken.None);

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task CountAvailableEquipmentUnitsAsync_ReturnsZeroForAnUnknownEquipmentType()
    {
        await using var context = await DatabaseTestContext.CreateAsync();

        var count = await BookingRules.CountAvailableEquipmentUnitsAsync(
            context.Database, refEquipmentId: 999, NowUtc, NowUtc.AddDays(1), CancellationToken.None);

        Assert.That(count, Is.Zero);
    }

    private sealed class DatabaseTestContext(
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

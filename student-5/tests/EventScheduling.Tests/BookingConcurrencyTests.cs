using EventScheduling.Database.Data;
using EventScheduling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Tests;
public sealed class BookingConcurrencyTests
{
    [Test]
    public async Task TryCommitRoomBookingAsync_UnderTwoSimultaneousRequests_OnlyOneSucceeds()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nfr03-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            await using (var setup = CreateFileContext(dbPath))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Rooms.Add(new Room { Id = 1, Name = "Study Room 101", Location = "Library", Capacity = 4, Description = "x" });
                await setup.SaveChangesAsync();
            }

            var start = new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
            var end = start.AddHours(1);

            async Task<bool> AttemptBookingAsync(int studentId)
            {
                await using var context = CreateFileContext(dbPath);
                var booking = new Booking
                {
                    StudentId = studentId,
                    RoomId = 1,
                    ResourceName = "Study Room 101",
                    StartUtc = start,
                    EndUtc = end,
                    Status = BookingStatus.Confirmed
                };

                return await BookingRules.TryCommitRoomBookingAsync(context, booking, CancellationToken.None);
            }

            var results = await Task.WhenAll(
                Task.Run(() => AttemptBookingAsync(4)),
                Task.Run(() => AttemptBookingAsync(14)));

            Assert.That(results.Count(succeeded => succeeded), Is.EqualTo(1),
                "exactly one of the two simultaneous requests should have won");

            await using var verify = CreateFileContext(dbPath);
            var confirmedCount = await verify.Bookings.CountAsync(
                booking => booking.RoomId == 1 && booking.Status == BookingStatus.Confirmed);
            Assert.That(confirmedCount, Is.EqualTo(1),
                "the room must end up with exactly one confirmed booking, not zero or two");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Test]
    public async Task TryCommitRoomBookingAsync_AllowsANonOverlappingBookingToCommitNormally()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nfr03-solo-{Guid.NewGuid():N}.db");
        try
        {
            await using (var setup = CreateFileContext(dbPath))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Rooms.Add(new Room { Id = 1, Name = "Study Room 101", Location = "Library", Capacity = 4, Description = "x" });
                await setup.SaveChangesAsync();
            }

            await using var context = CreateFileContext(dbPath);
            var start = new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);
            var booking = new Booking
            {
                StudentId = 4,
                RoomId = 1,
                ResourceName = "Study Room 101",
                StartUtc = start,
                EndUtc = start.AddHours(1),
                Status = BookingStatus.Confirmed
            };

            var succeeded = await BookingRules.TryCommitRoomBookingAsync(context, booking, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(succeeded, Is.True);
                Assert.That(booking.Id, Is.GreaterThan(0));
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static EventSchedulingDbContext CreateFileContext(string path)
    {
        var options = new DbContextOptionsBuilder<EventSchedulingDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new EventSchedulingDbContext(options);
    }
}

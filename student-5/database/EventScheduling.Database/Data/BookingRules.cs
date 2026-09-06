using EventScheduling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Database.Data;

public static class BookingRules
{
    public static bool HasSufficientNotice(DateTime startUtc, DateTime nowUtc) =>
        startUtc >= nowUtc.Add(Booking.MinBookingNotice);

    public static Task<int> CountActiveBookingsAsync(
        EventSchedulingDbContext database, int studentId, DateTime nowUtc, CancellationToken cancellationToken) =>
        database.Bookings.CountAsync(
            booking => booking.StudentId == studentId
                && booking.EndUtc >= nowUtc
                && (booking.Status == BookingStatus.Pending || booking.Status == BookingStatus.Confirmed),
            cancellationToken);

    public static bool HasCapacity(int? attendeeCount, int roomCapacity) =>
        attendeeCount is not int count || count <= roomCapacity;

    public static bool IsBeforeCutoff(DateTime bookingStartUtc, DateTime nowUtc) =>
        bookingStartUtc > nowUtc;
    public static Task<bool> HasRoomClashAsync(EventSchedulingDbContext database, int roomId, DateTime startUtc, DateTime endUtc,
        int? excludeBookingId, CancellationToken cancellationToken)
    {
        var query = database.Bookings.Where(other =>
            other.RoomId == roomId
            && other.Status != BookingStatus.Cancelled
            && other.Status != BookingStatus.Completed
            && other.Status != BookingStatus.Returned
            && startUtc < other.EndUtc
            && other.StartUtc < endUtc);

        if (excludeBookingId is int excludeId)
        {
            query = query.Where(other => other.Id != excludeId);
        }

        return query.AnyAsync(cancellationToken);
    }

    public static async Task<bool> TryCommitRoomBookingAsync(
        EventSchedulingDbContext database, Booking booking, CancellationToken cancellationToken)
    {
        await database.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        database.Bookings.Add(booking);
        await database.SaveChangesAsync(cancellationToken);

        var stillClashes = await HasRoomClashAsync(
            database, booking.RoomId!.Value, booking.StartUtc, booking.EndUtc,
            excludeBookingId: booking.Id, cancellationToken);

        if (stillClashes)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public static async Task<int> CountAvailableEquipmentUnitsAsync(
        EventSchedulingDbContext database, int refEquipmentId, DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var unitIds = await database.Equipment
            .Where(unit => unit.RefEquipmentId == refEquipmentId
                && unit.Status != EquipmentStatus.MTC && unit.Status != EquipmentStatus.RET)
            .Select(unit => unit.Id)
            .ToListAsync(cancellationToken);

        if (unitIds.Count == 0)
        {
            return 0;
        }

        var busyUnitIds = await database.Bookings
            .Where(booking => booking.EquipmentId != null
                && unitIds.Contains(booking.EquipmentId.Value)
                && booking.Status != BookingStatus.Cancelled
                && booking.Status != BookingStatus.Completed
                && booking.Status != BookingStatus.Returned
                && startUtc < booking.EndUtc
                && booking.StartUtc < endUtc)
            .Select(booking => booking.EquipmentId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);

        return unitIds.Count - busyUnitIds;
    }
}

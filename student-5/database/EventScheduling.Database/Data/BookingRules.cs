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

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
}

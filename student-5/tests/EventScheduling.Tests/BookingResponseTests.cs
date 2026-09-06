using EventScheduling.Database.Models;

namespace EventScheduling.Tests;

public sealed class BookingResponseTests
{
    private static Booking CreateBooking(
        int? roomId = null,
        int? equipmentId = null,
        int? refEquipmentId = null,
        BookingStatus status = BookingStatus.Confirmed,
        DateTime? endUtc = null) =>
        new()
        {
            Id = 1,
            StudentId = 4,
            RoomId = roomId,
            EquipmentId = equipmentId,
            RefEquipmentId = refEquipmentId,
            ResourceName = "Test",
            StartUtc = (endUtc ?? DateTime.UtcNow).AddHours(-2),
            EndUtc = endUtc ?? DateTime.UtcNow.AddDays(1),
            Status = status
        };

    [Test]
    public void ToResponse_MarksAConfirmedEquipmentHirePastItsEndAsOverdue()
    {
        var response = CreateBooking(
            equipmentId: 5, refEquipmentId: 2,
            status: BookingStatus.Confirmed,
            endUtc: DateTime.UtcNow.AddDays(-1)).ToResponse();

        Assert.That(response.Status, Is.EqualTo(BookingStatus.Overdue));
    }

    [Test]
    public void ToResponse_MarksAPendingEquipmentHirePastItsEndAsOverdue()
    {
        var response = CreateBooking(
            equipmentId: 5, refEquipmentId: 2,
            status: BookingStatus.Pending,
            endUtc: DateTime.UtcNow.AddDays(-1)).ToResponse();

        Assert.That(response.Status, Is.EqualTo(BookingStatus.Overdue));
    }

    [Test]
    public void ToResponse_DoesNotMarkAPastEquipmentHireOverdueOnceTheUnitIsReleased()
    {
        var response = CreateBooking(
            equipmentId: null, refEquipmentId: 2,
            status: BookingStatus.Returned,
            endUtc: DateTime.UtcNow.AddDays(-1)).ToResponse();

        Assert.That(response.Status, Is.EqualTo(BookingStatus.Returned));
    }

    [Test]
    public void ToResponse_NeverMarksARoomBookingOverdue()
    {
        var response = CreateBooking(
            roomId: 1,
            status: BookingStatus.Confirmed,
            endUtc: DateTime.UtcNow.AddDays(-5)).ToResponse();

        Assert.That(response.Status, Is.EqualTo(BookingStatus.Confirmed));
    }

    [Test]
    public void ToResponse_LeavesAFutureEquipmentHireConfirmed()
    {
        var response = CreateBooking(
            equipmentId: 5, refEquipmentId: 2,
            status: BookingStatus.Confirmed,
            endUtc: DateTime.UtcNow.AddDays(3)).ToResponse();

        Assert.That(response.Status, Is.EqualTo(BookingStatus.Confirmed));
    }

    [Test]
    public void ToResponse_ReportsResourceTypeRoomWhenRoomIdIsSet()
    {
        Assert.That(CreateBooking(roomId: 1).ToResponse().ResourceType, Is.EqualTo(ResourceType.Room));
    }

    [Test]
    public void ToResponse_ReportsResourceTypeEquipmentWhenRoomIdIsNull()
    {
        Assert.That(CreateBooking(equipmentId: 5, refEquipmentId: 2).ToResponse().ResourceType,
            Is.EqualTo(ResourceType.Equipment));
    }

    [Test]
    public void MaxEquipmentHire_IsSevenDays()
    {
        Assert.That(EventScheduling.Database.Models.Booking.MaxEquipmentHire, Is.EqualTo(TimeSpan.FromDays(7)));
    }
}

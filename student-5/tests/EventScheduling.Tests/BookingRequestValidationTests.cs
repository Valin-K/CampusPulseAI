using EventScheduling.Database.Models;

namespace EventScheduling.Tests;

public sealed class BookingRequestValidationTests
{
    private static readonly DateTime Start = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static BookingRequest Room(
        int studentId = 4,
        int? roomId = 1,
        int? refEquipmentId = null,
        string resourceName = "Study Room 101",
        DateTime? start = null,
        DateTime? end = null,
        BookingStatus status = BookingStatus.Confirmed,
        int? attendeeCount = null,
        string? conditionNotes = null) =>
        new(studentId, roomId, refEquipmentId, resourceName,
            start ?? Start, end ?? Start.AddHours(2), status,
            AttendeeCount: attendeeCount, ConditionNotes: conditionNotes);

    [Test]
    public void Validate_PassesForAWellFormedRoomBooking()
    {
        Assert.That(Room().Validate(), Is.Null);
    }

    [Test]
    public void Validate_PassesForAWellFormedEquipmentBooking()
    {
        var request = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector",
            end: Start.AddDays(7));

        Assert.That(request.Validate(), Is.Null);
    }

    [Test]
    public void Validate_RejectsNonPositiveStudentId()
    {
        Assert.That(Room(studentId: 0).Validate(), Is.EqualTo("Student ID must be greater than zero."));
    }

    [TestCase(BookingStatus.Overdue)]
    [TestCase(BookingStatus.Completed)]
    [TestCase(BookingStatus.Returned)]
    public void Validate_RejectsSystemManagedStatuses(BookingStatus status)
    {
        Assert.That(Room(status: status).Validate(),
            Is.EqualTo("Overdue, Completed and Returned are managed by the system and cannot be set directly."));
    }

    [Test]
    public void Validate_RejectsBookingWithNeitherRoomNorEquipment()
    {
        Assert.That(Room(roomId: null, refEquipmentId: null).Validate(),
            Is.EqualTo("Exactly one of room or equipment type must be specified."));
    }

    [Test]
    public void Validate_RejectsBookingWithBothRoomAndEquipment()
    {
        Assert.That(Room(roomId: 1, refEquipmentId: 3).Validate(),
            Is.EqualTo("Exactly one of room or equipment type must be specified."));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Validate_RejectsMissingResourceName(string name)
    {
        Assert.That(Room(resourceName: name).Validate(),
            Is.EqualTo("Resource name is required and must be 120 characters or fewer."));
    }

    [Test]
    public void Validate_RejectsResourceNameOver120Characters()
    {
        Assert.That(Room(resourceName: new string('x', 121)).Validate(),
            Is.EqualTo("Resource name is required and must be 120 characters or fewer."));
    }

    [Test]
    public void Validate_RejectsEndTimeNotAfterStart()
    {
        Assert.That(Room(start: Start, end: Start).Validate(),
            Is.EqualTo("End time must be after the start time."));
    }

    [Test]
    public void Validate_RejectsEquipmentHireLongerThanSevenDays()
    {
        var request = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector",
            start: Start, end: Start.AddDays(7).AddMinutes(1));

        Assert.That(request.Validate(), Is.EqualTo("Equipment can be hired for a maximum of 7 days."));
    }

    [Test]
    public void Validate_RejectsRoomBookingLongerThanMaxDuration()
    {
        var request = Room(start: Start, end: Start.Add(Booking.MaxRoomBookingDuration).AddMinutes(1));

        Assert.That(request.Validate(),
            Is.EqualTo($"A single room booking can run for at most {Booking.MaxRoomBookingDuration.TotalHours:0.#} hours."));
    }

    [Test]
    public void Validate_AllowsRoomBookingUpToMaxDuration()
    {
        var request = Room(start: Start, end: Start.Add(Booking.MaxRoomBookingDuration));

        Assert.That(request.Validate(), Is.Null);
    }

    [Test]
    public void Validate_EquipmentHireIsNotSubjectToTheRoomDurationCap()
    {
        var request = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector",
            start: Start, end: Start.AddDays(7));

        Assert.That(request.Validate(), Is.Null);
    }

    [Test]
    public void ToBooking_TrimsTheResourceNameAndCopiesFields()
    {
        var booking = Room(resourceName: "  Study Room 101  ").ToBooking();

        Assert.Multiple(() =>
        {
            Assert.That(booking.ResourceName, Is.EqualTo("Study Room 101"));
            Assert.That(booking.RoomId, Is.EqualTo(1));
            Assert.That(booking.Status, Is.EqualTo(BookingStatus.Confirmed));
            Assert.That(booking.EquipmentId, Is.Null);
        });
    }

    // FR-09: attendee count.

    [TestCase(0)]
    [TestCase(-1)]
    public void Validate_RejectsNonPositiveAttendeeCount(int count)
    {
        Assert.That(Room(attendeeCount: count).Validate(), Is.EqualTo("Attendee count must be greater than zero."));
    }

    [Test]
    public void Validate_PassesForAPositiveAttendeeCount()
    {
        Assert.That(Room(attendeeCount: 4).Validate(), Is.Null);
    }

    [Test]
    public void Validate_RejectsAttendeeCountOnAnEquipmentBooking()
    {
        var request = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector", attendeeCount: 4);

        Assert.That(request.Validate(), Is.EqualTo("Attendee count only applies to room bookings."));
    }

    [Test]
    public void ToBooking_CopiesTheAttendeeCount()
    {
        Assert.That(Room(attendeeCount: 6).ToBooking().AttendeeCount, Is.EqualTo(6));
    }

    // FR-21: condition notes.

    [Test]
    public void Validate_RejectsConditionNotesOverTheMaxLength()
    {
        var request = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector",
            conditionNotes: new string('x', Booking.MaxConditionNotesLength + 1));

        Assert.That(request.Validate(),
            Is.EqualTo($"Condition notes must be {Booking.MaxConditionNotesLength} characters or fewer."));
    }

    [Test]
    public void Validate_PassesForConditionNotesAtTheMaxLength()
    {
        var request = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector",
            conditionNotes: new string('x', Booking.MaxConditionNotesLength));

        Assert.That(request.Validate(), Is.Null);
    }

    [Test]
    public void ToBooking_TrimsAndCopiesConditionNotesAsCheckOutNotes()
    {
        var booking = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector",
            conditionNotes: "  Small scuff on the case.  ").ToBooking();

        Assert.That(booking.CheckOutConditionNotes, Is.EqualTo("Small scuff on the case."));
    }

    [Test]
    public void ToBooking_TreatsWhitespaceOnlyConditionNotesAsNull()
    {
        var booking = Room(roomId: null, refEquipmentId: 3, resourceName: "Portable Projector",
            conditionNotes: "   ").ToBooking();

        Assert.That(booking.CheckOutConditionNotes, Is.Null);
    }
}

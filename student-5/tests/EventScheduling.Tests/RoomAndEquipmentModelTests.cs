using EventScheduling.Database.Models;

namespace EventScheduling.Tests;

public sealed class RoomAndEquipmentModelTests
{
    [TestCase(RoomKind.Study, false)]
    [TestCase(RoomKind.Lab, true)]
    [TestCase(RoomKind.Lecture, true)]
    [TestCase(RoomKind.Collaborative, false)]
    public void Room_IsStaffRoom_IsTrueOnlyForLabAndLecture(RoomKind kind, bool expected)
    {
        Assert.That(new Room { Kind = kind }.IsStaffRoom, Is.EqualTo(expected));
    }

    [Test]
    public void Room_DefaultsToAStudyRoom()
    {
        Assert.That(new Room().Kind, Is.EqualTo(RoomKind.Study));
    }

    [Test]
    public void Room_DefaultsToActive()
    {
        Assert.That(new Room().IsActive, Is.True);
    }

    [Test]
    public void Room_DefaultsToNoOpeningHours()
    {
        Assert.That(new Room().OpeningHours, Is.Empty);
    }

    private static RoomRequest RoomRequest(string name = "Study Room 9", string location = "Library",
        int capacity = 4, RoomKind kind = RoomKind.Study, string openingHours = "") =>
        new(name, location, capacity, "A room.", kind, OpeningHours: openingHours);

    [Test]
    public void RoomRequest_Validate_PassesForAWellFormedRoom()
    {
        Assert.That(RoomRequest().Validate(), Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void RoomRequest_Validate_RejectsMissingName(string name)
    {
        Assert.That(RoomRequest(name: name).Validate(),
            Is.EqualTo("Name is required and must be 120 characters or fewer."));
    }

    [Test]
    public void RoomRequest_Validate_RejectsMissingLocation()
    {
        Assert.That(RoomRequest(location: " ").Validate(),
            Is.EqualTo("Location is required and must be 120 characters or fewer."));
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void RoomRequest_Validate_RejectsNonPositiveCapacity(int capacity)
    {
        Assert.That(RoomRequest(capacity: capacity).Validate(),
            Is.EqualTo("Capacity must be greater than zero."));
    }

    [Test]
    public void RoomRequest_ToRoom_TrimsAndKeepsTheKind()
    {
        var room = new RoomRequest("  Physics Lab  ", "  Science Building  ", 20, "  Bench lab  ", RoomKind.Lab)
            .ToRoom();

        Assert.Multiple(() =>
        {
            Assert.That(room.Name, Is.EqualTo("Physics Lab"));
            Assert.That(room.Location, Is.EqualTo("Science Building"));
            Assert.That(room.Description, Is.EqualTo("Bench lab"));
            Assert.That(room.Kind, Is.EqualTo(RoomKind.Lab));
        });
    }

    [Test]
    public void RoomRequest_DefaultsToActive()
    {
        Assert.That(RoomRequest().IsActive, Is.True);
    }

    [Test]
    public void RoomRequest_ToRoom_CarriesThroughAnInactiveFlag()
    {
        var room = new RoomRequest("Study Room 9", "Library", 4, "A room.", RoomKind.Study, IsActive: false).ToRoom();

        Assert.That(room.IsActive, Is.False);
    }

    [Test]
    public void RoomRequest_Validate_RejectsOpeningHoursOver100Characters()
    {
        Assert.That(RoomRequest(openingHours: new string('x', 101)).Validate(),
            Is.EqualTo("Opening hours must be 100 characters or fewer."));
    }

    [Test]
    public void RoomRequest_Validate_PassesForOpeningHoursAt100Characters()
    {
        Assert.That(RoomRequest(openingHours: new string('x', 100)).Validate(), Is.Null);
    }

    [Test]
    public void RoomRequest_ToRoom_TrimsAndCopiesOpeningHours()
    {
        var room = new RoomRequest("Study Room 9", "Library", 4, "A room.", OpeningHours: "  Mon-Fri 8am-8pm  ")
            .ToRoom();

        Assert.That(room.OpeningHours, Is.EqualTo("Mon-Fri 8am-8pm"));
    }

    [Test]
    public void Room_UpdateFrom_CopiesOpeningHours()
    {
        var room = new Room { Name = "Study Room 9", Location = "Library", Capacity = 4, Description = "A room." };

        room.UpdateFrom(new RoomRequest("Study Room 9", "Library", 4, "A room.", OpeningHours: "9am-5pm"));

        Assert.That(room.OpeningHours, Is.EqualTo("9am-5pm"));
    }

    [Test]
    public void Room_ToResponse_IncludesOpeningHours()
    {
        var room = new Room { Name = "Study Room 9", Location = "Library", Capacity = 4, Description = "x", OpeningHours = "24/7" };

        Assert.That(room.ToResponse().OpeningHours, Is.EqualTo("24/7"));
    }

    [Test]
    public void Room_DefaultsToNotAccessible()
    {
        Assert.That(new Room().IsAccessible, Is.False);
    }

    [Test]
    public void RoomRequest_DefaultsToNotAccessible()
    {
        Assert.That(RoomRequest().IsAccessible, Is.False);
    }

    [Test]
    public void RoomRequest_ToRoom_CarriesThroughAccessible()
    {
        var room = new RoomRequest("Study Room 9", "Library", 4, "A room.", IsAccessible: true).ToRoom();

        Assert.That(room.IsAccessible, Is.True);
    }

    [Test]
    public void Room_UpdateFrom_CopiesAccessible()
    {
        var room = new Room { Name = "Study Room 9", Location = "Library", Capacity = 4, Description = "A room." };

        room.UpdateFrom(new RoomRequest("Study Room 9", "Library", 4, "A room.", IsAccessible: true));

        Assert.That(room.IsAccessible, Is.True);
    }

    [Test]
    public void Room_ToResponse_IncludesAccessible()
    {
        var room = new Room { Name = "Study Room 9", Location = "Library", Capacity = 4, Description = "x", IsAccessible = true };

        Assert.That(room.ToResponse().IsAccessible, Is.True);
    }

    [Test]
    public void Room_UpdateFrom_CanDeactivateARoomWithoutChangingItsIdentity()
    {
        var room = new Room { Name = "Study Room 9", Location = "Library", Capacity = 4, Description = "A room." };

        room.UpdateFrom(new RoomRequest("Study Room 9", "Library", 4, "A room.", RoomKind.Study, IsActive: false));

        Assert.That(room.IsActive, Is.False);
    }

    [Test]
    public void Room_ToResponse_IncludesTheActiveFlag()
    {
        var room = new Room { Name = "Study Room 9", Location = "Library", Capacity = 4, Description = "x", IsActive = false };

        Assert.That(room.ToResponse().IsActive, Is.False);
    }

    [Test]
    public void RefEquipmentRequest_Validate_PassesForAWellFormedType()
    {
        Assert.That(new RefEquipmentRequest("HDMI Adapter", "Cables", "USB-C to HDMI.").Validate(), Is.Null);
    }

    [Test]
    public void RefEquipmentRequest_Validate_RejectsMissingName()
    {
        Assert.That(new RefEquipmentRequest(" ", "Cables", "x").Validate(),
            Is.EqualTo("Name is required and must be 120 characters or fewer."));
    }

    [Test]
    public void RefEquipmentRequest_Validate_RejectsMissingCategory()
    {
        Assert.That(new RefEquipmentRequest("HDMI Adapter", "", "x").Validate(),
            Is.EqualTo("Category is required and must be 80 characters or fewer."));
    }

    [Test]
    public void EquipmentRequest_Validate_PassesForAWellFormedUnit()
    {
        Assert.That(new EquipmentRequest(1, "HDMI-013", EquipmentStatus.AVL, null).Validate(), Is.Null);
    }

    [Test]
    public void EquipmentRequest_Validate_RejectsMissingEquipmentType()
    {
        Assert.That(new EquipmentRequest(0, "HDMI-013", EquipmentStatus.AVL, null).Validate(),
            Is.EqualTo("A valid equipment type is required."));
    }

    [Test]
    public void EquipmentRequest_Validate_RejectsMissingSerialNumber()
    {
        Assert.That(new EquipmentRequest(1, "  ", EquipmentStatus.AVL, null).Validate(),
            Is.EqualTo("Serial number is required and must be 80 characters or fewer."));
    }
}

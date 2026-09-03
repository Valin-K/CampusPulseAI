using EventScheduling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Database.Data;

public static class EventSeedData
{
    private const int StudentId = 4;

    public static async Task InitializeAsync(EventSchedulingDbContext database)
    {
        if (!await database.Rooms.AnyAsync())
        {
            database.Rooms.AddRange(
                Room(1, "Study Room 101", "Library, Level 1", 4, "Quiet room with a whiteboard, ideal for small group study.", RoomKind.Study),
                Room(2, "Study Room 204", "Library, Level 2", 6, "Booth seating with a wall-mounted display for group work.", RoomKind.Study),
                Room(3, "Group Pod A", "Student Centre, Level 3", 8, "Open pod with movable furniture and a large screen.", RoomKind.Study),
                Room(4, "Silent Study Room 3B", "Library, Level 3", 2, "Single-occupant silent room for focused study.", RoomKind.Study),
                Room(5, "Collaboration Suite C", "Engineering Building, Level 1", 10, "Large suite with breakout seating and dual displays.", RoomKind.Study),
                Room(6, "Study Room 302", "Library, Level 3", 4, "Corner room with natural light and a small whiteboard.", RoomKind.Study),
                Room(7, "Group Pod B", "Student Centre, Level 3", 8, "Open pod beside Pod A with movable furniture and a screen.", RoomKind.Study),
                Room(8, "Media Booth 1", "Library, Level 1", 3, "Soundproofed booth for recording and editing group projects.", RoomKind.Study),
                Room(9, "Computer Lab 1", "Engineering Building, Level 2", 30, "30 workstations with dual monitors for programming classes.", RoomKind.Lab),
                Room(10, "Computer Lab 2", "Engineering Building, Level 2", 24, "24 workstations configured for networking and security labs.", RoomKind.Lab),
                Room(11, "Physics Lab", "Science Building, Level 1", 20, "Bench lab with power rigs and measurement equipment.", RoomKind.Lab),
                Room(12, "Chemistry Lab", "Science Building, Level 2", 18, "Wet lab with fume hoods for supervised chemistry practicals.", RoomKind.Lab),
                Room(13, "Lecture Theatre A", "Central Hall, Ground Floor", 180, "Tiered theatre with a lectern, projector and lecture-capture.", RoomKind.Lecture),
                Room(14, "Lecture Theatre B", "Central Hall, Level 1", 120, "Tiered theatre with dual projection and a document camera.", RoomKind.Lecture),
                Room(15, "Seminar Room 210", "Arts Building, Level 2", 40, "Flat-floor seminar room with movable tables and a screen.", RoomKind.Lecture));
        }

        if (!await database.RefEquipment.AnyAsync())
        {
            database.RefEquipment.AddRange(
                RefEquipment(1, "HDMI Adapter", "Cables & Adapters", "USB-C to HDMI adapter for laptop presentations."),
                RefEquipment(2, "Laptop Charger", "Power", "Universal laptop charger with multiple tips."),
                RefEquipment(3, "Portable Projector", "Presentation", "Compact projector for small group presentations."),
                RefEquipment(4, "Whiteboard Markers Kit", "Stationery", "Set of 8 dry-erase markers and an eraser."),
                RefEquipment(5, "Camera Tripod", "Media", "Adjustable tripod for cameras and phones."),
                RefEquipment(6, "DSLR Camera", "Media", "Entry-level DSLR camera kit for student media projects."));
        }

        if (!await database.Equipment.AnyAsync())
        {
            // Units held by an active/overdue seed booking (bookings 3, 5, 8, 11, 12 below) start Occupied.
            var occupiedUnitIds = new HashSet<int> { 1, 2, 13, 21, 44 };
            var unitTypes = new[]
            {
                (RefEquipmentId: 1, SerialPrefix: "HDMI", Count: 12),
                (RefEquipmentId: 2, SerialPrefix: "CHG", Count: 8),
                (RefEquipmentId: 3, SerialPrefix: "PRJ", Count: 3),
                (RefEquipmentId: 4, SerialPrefix: "WMK", Count: 15),
                (RefEquipmentId: 5, SerialPrefix: "TRP", Count: 5),
                (RefEquipmentId: 6, SerialPrefix: "DSLR", Count: 2)
            };

            var unitId = 1;
            foreach (var type in unitTypes)
            {
                for (var index = 1; index <= type.Count; index++, unitId++)
                {
                    database.Equipment.Add(Equipment(
                        unitId,
                        type.RefEquipmentId,
                        $"{type.SerialPrefix}-{index:D3}",
                        occupiedUnitIds.Contains(unitId) ? EquipmentStatus.OCC : EquipmentStatus.AVL));
                }
            }
        }

        if (!await database.Bookings.AnyAsync())
        {
            database.Bookings.AddRange(
                RoomBooking(1, 1, "Study Room 101", new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc), new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(2, 3, "Group Pod A", new(2026, 9, 1, 14, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                EquipmentBooking(3, refEquipmentId: 3, equipmentId: 21, "Portable Projector", new(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 2, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Pending),
                RoomBooking(4, 4, "Silent Study Room 3B", new(2026, 9, 3, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 3, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                EquipmentBooking(5, refEquipmentId: 6, equipmentId: 44, "DSLR Camera", new(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 5, 17, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(6, 2, "Study Room 204", new(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc), new(2026, 9, 8, 13, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(7, 5, "Collaboration Suite C", new(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc), new(2026, 9, 12, 17, 0, 0, DateTimeKind.Utc), BookingStatus.Pending),
                EquipmentBooking(8, refEquipmentId: 1, equipmentId: 1, "HDMI Adapter", new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(9, 1, "Study Room 101", new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                ReleasedEquipmentBooking(10, refEquipmentId: 4, "Whiteboard Markers Kit", new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc), new(2026, 9, 28, 13, 0, 0, DateTimeKind.Utc), BookingStatus.Cancelled),
                EquipmentBooking(11, refEquipmentId: 2, equipmentId: 13, "Laptop Charger", new(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc), new(2026, 8, 27, 17, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                EquipmentBooking(12, refEquipmentId: 1, equipmentId: 2, "HDMI Adapter", new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(13, 1, "Study Room 101", new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(14, 2, "Study Room 204", new(2026, 9, 1, 15, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(15, 4, "Silent Study Room 3B", new(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 19, 0, 0, DateTimeKind.Utc), BookingStatus.Pending));
        }

        await database.SaveChangesAsync();
    }

    private static Room Room(int id, string name, string location, int capacity, string description, RoomKind kind) => new()
    {
        Id = id,
        Name = name,
        Location = location,
        Capacity = capacity,
        Description = description,
        Kind = kind
    };

    private static RefEquipment RefEquipment(int id, string name, string category, string description) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        Description = description
    };

    private static Equipment Equipment(int id, int refEquipmentId, string serialNumber, EquipmentStatus status) => new()
    {
        Id = id,
        RefEquipmentId = refEquipmentId,
        SerialNumber = serialNumber,
        Status = status,
        CreatedByStaffId = null
    };

    private static Booking RoomBooking(int id, int roomId, string resourceName, DateTime startUtc, DateTime endUtc, BookingStatus status) => new()
    {
        Id = id,
        StudentId = StudentId,
        RoomId = roomId,
        EquipmentId = null,
        RefEquipmentId = null,
        ResourceName = resourceName,
        StartUtc = startUtc,
        EndUtc = endUtc,
        Status = status
    };

    private static Booking EquipmentBooking(int id, int refEquipmentId, int equipmentId, string resourceName, DateTime startUtc, DateTime endUtc, BookingStatus status) => new()
    {
        Id = id,
        StudentId = StudentId,
        RoomId = null,
        EquipmentId = equipmentId,
        RefEquipmentId = refEquipmentId,
        ResourceName = resourceName,
        StartUtc = startUtc,
        EndUtc = endUtc,
        Status = status
    };

    private static Booking ReleasedEquipmentBooking(int id, int refEquipmentId, string resourceName, DateTime startUtc, DateTime endUtc, BookingStatus status) => new()
    {
        Id = id,
        StudentId = StudentId,
        RoomId = null,
        EquipmentId = null,
        RefEquipmentId = refEquipmentId,
        ResourceName = resourceName,
        StartUtc = startUtc,
        EndUtc = endUtc,
        Status = status
    };
}

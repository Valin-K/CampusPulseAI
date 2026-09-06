using EventScheduling.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace EventScheduling.Database.Data;

public static class EventSeedData
{
    private const int StudentId = 4;
    private const int TeacherId = 14;

    public static async Task InitializeAsync(EventSchedulingDbContext database)
    {
        if (!await database.Rooms.AnyAsync())
        {
            database.Rooms.AddRange(
                Room(1, "Study Room 101", "Library, Level 1", 4, "Quiet room with a whiteboard, ideal for small group study.", RoomKind.Study, isAccessible: true),
                Room(2, "Study Room 204", "Library, Level 2", 6, "Booth seating with a wall-mounted display for group work.", RoomKind.Study),
                Room(3, "Group Pod A", "Student Centre, Level 3", 8, "Open pod with movable furniture and a large screen.", RoomKind.Collaborative),
                Room(4, "Silent Study Room 3B", "Library, Level 3", 2, "Single-occupant silent room for focused study.", RoomKind.Study),
                Room(5, "Collaboration Suite C", "Engineering Building, Level 1", 10, "Large suite with breakout seating and dual displays.", RoomKind.Collaborative, isAccessible: true),
                Room(6, "Study Room 302", "Library, Level 3", 4, "Corner room with natural light and a small whiteboard.", RoomKind.Study),
                Room(7, "Group Pod B", "Student Centre, Level 3", 8, "Open pod beside Pod A with movable furniture and a screen.", RoomKind.Collaborative),
                Room(8, "Media Booth 1", "Library, Level 1", 3, "Soundproofed booth for recording and editing group projects.", RoomKind.Study, isAccessible: true),
                Room(9, "Computer Lab 1", "Engineering Building, Level 2", 30, "30 workstations with dual monitors for programming classes.", RoomKind.Lab),
                Room(10, "Computer Lab 2", "Engineering Building, Level 2", 24, "24 workstations configured for networking and security labs.", RoomKind.Lab),
                Room(11, "Physics Lab", "Science Building, Level 1", 20, "Bench lab with power rigs and measurement equipment.", RoomKind.Lab, isAccessible: true),
                Room(12, "Chemistry Lab", "Science Building, Level 2", 18, "Wet lab with fume hoods for supervised chemistry practicals.", RoomKind.Lab),
                Room(13, "Lecture Theatre A", "Central Hall, Ground Floor", 180, "Tiered theatre with a lectern, projector and lecture-capture.", RoomKind.Lecture, isAccessible: true),
                Room(14, "Lecture Theatre B", "Central Hall, Level 1", 120, "Tiered theatre with dual projection and a document camera.", RoomKind.Lecture, isAccessible: true),
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
                RefEquipment(6, "DSLR Camera", "Media", "Entry-level DSLR camera kit for student media projects."),
                RefEquipment(7, "Wireless Presenter", "Presentation", "Handheld clicker with laser pointer and slide navigation."),
                RefEquipment(8, "Conference Speakerphone", "Audio", "USB and Bluetooth omnidirectional speakerphone for hybrid classes."),
                RefEquipment(9, "Lapel Microphone Kit", "Audio", "Clip-on wireless microphone with belt-pack receiver for lectures."),
                RefEquipment(10, "Extension Cord Reel", "Power", "10 m heavy-duty extension lead on a retractable reel."),
                RefEquipment(11, "Document Scanner", "Media", "Portable duplex sheet-fed scanner for handouts and forms."));
        }

        if (!await database.Equipment.AnyAsync())
        {
            var occupiedUnitIds = new HashSet<int> { 1, 2, 13, 21, 22, 39, 40, 44, 45 };
            var unitTypes = new[]
            {
                (RefEquipmentId: 1, SerialPrefix: "HDMI", Count: 12),
                (RefEquipmentId: 2, SerialPrefix: "CHG", Count: 8),
                (RefEquipmentId: 3, SerialPrefix: "PRJ", Count: 3),
                (RefEquipmentId: 4, SerialPrefix: "WMK", Count: 15),
                (RefEquipmentId: 5, SerialPrefix: "TRP", Count: 5),
                (RefEquipmentId: 6, SerialPrefix: "DSLR", Count: 2),
                (RefEquipmentId: 7, SerialPrefix: "WPR", Count: 5),
                (RefEquipmentId: 8, SerialPrefix: "SPK", Count: 5),
                (RefEquipmentId: 9, SerialPrefix: "MIC", Count: 5),
                (RefEquipmentId: 10, SerialPrefix: "EXT", Count: 5),
                (RefEquipmentId: 11, SerialPrefix: "SCN", Count: 5)
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
                RoomBooking(1, 1, "Study Room 101", new(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc), new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Completed),
                RoomBooking(2, 3, "Group Pod A", new(2026, 9, 1, 14, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(3, 2, "Study Room 204", new(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 2, 10, 30, 0, DateTimeKind.Utc), BookingStatus.Cancelled),
                EquipmentBooking(4, refEquipmentId: 2, equipmentId: 13, "Laptop Charger", new(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc), new(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                ReleasedEquipmentBooking(5, refEquipmentId: 6, "DSLR Camera", new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), BookingStatus.Returned),
                ReleasedEquipmentBooking(6, refEquipmentId: 4, "Whiteboard Markers Kit", new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc), BookingStatus.Cancelled),

                RoomBooking(7, 4, "Silent Study Room 3B", new(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc), new(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(8, 1, "Study Room 101", new(2026, 9, 3, 11, 0, 0, DateTimeKind.Utc), new(2026, 9, 3, 13, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(9, 7, "Group Pod B", new(2026, 9, 3, 15, 0, 0, DateTimeKind.Utc), new(2026, 9, 3, 17, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                EquipmentBooking(10, refEquipmentId: 1, equipmentId: 1, "HDMI Adapter", new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 6, 9, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                EquipmentBooking(12, refEquipmentId: 5, equipmentId: 39, "Camera Tripod", new(2026, 9, 3, 6, 0, 0, DateTimeKind.Utc), new(2026, 9, 8, 6, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),

                EquipmentBooking(11, refEquipmentId: 3, equipmentId: 21, "Portable Projector", new(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 12, 9, 0, 0, DateTimeKind.Utc), BookingStatus.Pending),

                RoomBooking(13, 6, "Study Room 302", new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(14, 5, "Collaboration Suite C", new(2026, 9, 11, 14, 0, 0, DateTimeKind.Utc), new(2026, 9, 11, 16, 0, 0, DateTimeKind.Utc), BookingStatus.Pending),
                RoomBooking(15, 8, "Media Booth 1", new(2026, 9, 16, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 16, 14, 30, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                EquipmentBooking(16, refEquipmentId: 6, equipmentId: 44, "DSLR Camera", new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(17, 2, "Study Room 204", new(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 23, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                EquipmentBooking(18, refEquipmentId: 6, equipmentId: 45, "DSLR Camera", new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed),
                RoomBooking(19, 1, "Study Room 101", new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), new(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc), BookingStatus.Pending),

                RoomBooking(20, 13, "Lecture Theatre A", new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Completed, TeacherId),
                RoomBooking(21, 9, "Computer Lab 1", new(2026, 9, 2, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 2, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(22, 11, "Physics Lab", new(2026, 9, 2, 15, 30, 0, DateTimeKind.Utc), new(2026, 9, 2, 17, 0, 0, DateTimeKind.Utc), BookingStatus.Cancelled, TeacherId),

                RoomBooking(23, 10, "Computer Lab 2", new(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc), new(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(24, 14, "Lecture Theatre B", new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc), new(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(25, 15, "Seminar Room 210", new(2026, 9, 3, 16, 0, 0, DateTimeKind.Utc), new(2026, 9, 3, 18, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                EquipmentBooking(26, refEquipmentId: 1, equipmentId: 2, "HDMI Adapter", new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), new(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),

                RoomBooking(27, 13, "Lecture Theatre A", new(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 7, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(28, 13, "Lecture Theatre A", new(2026, 9, 9, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 9, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(29, 13, "Lecture Theatre A", new(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 14, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(30, 13, "Lecture Theatre A", new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 16, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(31, 13, "Lecture Theatre A", new(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 21, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(32, 13, "Lecture Theatre A", new(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 23, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(33, 13, "Lecture Theatre A", new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 28, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(34, 13, "Lecture Theatre A", new(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 30, 11, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),

                RoomBooking(35, 9, "Computer Lab 1", new(2026, 9, 8, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 8, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(36, 9, "Computer Lab 1", new(2026, 9, 10, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(37, 9, "Computer Lab 1", new(2026, 9, 15, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 15, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(38, 9, "Computer Lab 1", new(2026, 9, 17, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 17, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(39, 9, "Computer Lab 1", new(2026, 9, 22, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 22, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(40, 9, "Computer Lab 1", new(2026, 9, 24, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 24, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(41, 9, "Computer Lab 1", new(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc), new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(42, 9, "Computer Lab 1", new(2026, 10, 1, 13, 0, 0, DateTimeKind.Utc), new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),

                RoomBooking(43, 11, "Physics Lab", new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(44, 11, "Physics Lab", new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(45, 11, "Physics Lab", new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(46, 11, "Physics Lab", new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                RoomBooking(47, 11, "Physics Lab", new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc), new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),

                RoomBooking(48, 12, "Chemistry Lab", new(2026, 9, 15, 14, 0, 0, DateTimeKind.Utc), new(2026, 9, 15, 16, 0, 0, DateTimeKind.Utc), BookingStatus.Pending, TeacherId),
                RoomBooking(49, 14, "Lecture Theatre B", new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc), new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Cancelled, TeacherId),
                EquipmentBooking(50, refEquipmentId: 3, equipmentId: 22, "Portable Projector", new(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 19, 9, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId),
                EquipmentBooking(51, refEquipmentId: 5, equipmentId: 40, "Camera Tripod", new(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc), new(2026, 9, 12, 8, 0, 0, DateTimeKind.Utc), BookingStatus.Confirmed, TeacherId));
        }

        await database.SaveChangesAsync();
    }

    private static Room Room(int id, string name, string location, int capacity, string description, RoomKind kind,
        string openingHours = "Mon-Fri 7am-10pm, Sat-Sun 9am-6pm", bool isAccessible = false) => new()
    {
        Id = id,
        Name = name,
        Location = location,
        Capacity = capacity,
        Description = description,
        Kind = kind,
        OpeningHours = openingHours,
        IsAccessible = isAccessible
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

    private static Booking RoomBooking(int id, int roomId, string resourceName, DateTime startUtc, DateTime endUtc, BookingStatus status, int userId = StudentId) => new()
    {
        Id = id,
        StudentId = userId,
        RoomId = roomId,
        EquipmentId = null,
        RefEquipmentId = null,
        ResourceName = resourceName,
        StartUtc = startUtc,
        EndUtc = endUtc,
        Status = status
    };

    private static Booking EquipmentBooking(int id, int refEquipmentId, int equipmentId, string resourceName, DateTime startUtc, DateTime endUtc, BookingStatus status, int userId = StudentId) => new()
    {
        Id = id,
        StudentId = userId,
        RoomId = null,
        EquipmentId = equipmentId,
        RefEquipmentId = refEquipmentId,
        ResourceName = resourceName,
        StartUtc = startUtc,
        EndUtc = endUtc,
        Status = status
    };

    private static Booking ReleasedEquipmentBooking(int id, int refEquipmentId, string resourceName, DateTime startUtc, DateTime endUtc, BookingStatus status, int userId = StudentId) => new()
    {
        Id = id,
        StudentId = userId,
        RoomId = null,
        EquipmentId = null,
        RefEquipmentId = refEquipmentId,
        ResourceName = resourceName,
        StartUtc = startUtc,
        EndUtc = endUtc,
        Status = status
    };
}

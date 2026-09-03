namespace CampusHousing.Frontend.Models;

public sealed class DormRoom { public int Id { get; set; } public string Building { get; set; } = ""; public string RoomNumber { get; set; } = ""; public int Capacity { get; set; } public int OccupiedBeds { get; set; } public bool IsAvailable { get; set; } }
public sealed class HousingAllocation { public int Id { get; set; } public int DormRoomId { get; set; } public string StudentId { get; set; } = ""; public string StudentName { get; set; } = ""; public string? RoommatePreference { get; set; } public DateTime AllocatedAt { get; set; } }
public sealed class MaintenanceTicket { public int Id { get; set; } public string StudentId { get; set; } = ""; public int? DormRoomId { get; set; } public string Description { get; set; } = ""; public string Severity { get; set; } = ""; public string RepairQueue { get; set; } = ""; public string Status { get; set; } = ""; public DateTime CreatedAt { get; set; } public DateTime? ResolvedAt { get; set; } }

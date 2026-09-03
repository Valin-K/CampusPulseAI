namespace CampusHousing.Database.Models;

public sealed class DormRoom
{
    public int Id { get; set; }
    public string Building { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public sealed class HousingAllocation
{
    public int Id { get; set; }
    public int DormRoomId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? RoommatePreference { get; set; }
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MaintenanceTicket
{
    public int Id { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public int? DormRoomId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "Low";
    public string RepairQueue { get; set; } = "General Maintenance";
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

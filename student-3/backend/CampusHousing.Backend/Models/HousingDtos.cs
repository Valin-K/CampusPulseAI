namespace CampusHousing.Backend.Models;

public sealed record DormRoom(int Id, string Building, string RoomNumber, int Capacity, bool IsAvailable);
public sealed record HousingAllocation(int Id, int DormRoomId, string StudentId, string StudentName, string? RoommatePreference, DateTime AllocatedAt);
public sealed record MaintenanceTicket(int Id, string StudentId, int? DormRoomId, string Description, string Severity, string RepairQueue, string Status, DateTime CreatedAt, DateTime? ResolvedAt);
public sealed record CreateAllocationRequest(int DormRoomId, string StudentId, string StudentName, string? RoommatePreference);
public sealed record CreateMaintenanceRequest(string StudentId, int? DormRoomId, string Description);
public sealed record UpdateTicketRequest(string Status, string? Severity = null, string? RepairQueue = null);
public sealed record UpdateAllocationRequest(string StudentId, string StudentName, string? RoommatePreference);
public sealed record CreateRoomRequest(string Building, string RoomNumber, int Capacity, bool IsAvailable = true);

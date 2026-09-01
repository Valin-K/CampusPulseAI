namespace EventScheduling.Database.Models;

public sealed class Equipment
{
    public int Id { get; set; }
    public int RefEquipmentId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public EquipmentStatus Status { get; set; }
    public int? CreatedByStaffId { get; set; }

    public void UpdateFrom(EquipmentRequest request)
    {
        RefEquipmentId = request.RefEquipmentId;
        SerialNumber = request.SerialNumber.Trim();
        Status = request.Status;
        CreatedByStaffId = request.CreatedByStaffId;
    }

    public EquipmentResponse ToResponse() =>
        new(Id, RefEquipmentId, SerialNumber, Status, CreatedByStaffId);
}

public enum EquipmentStatus
{
    AVL,
    OCC,
    MTC,
    RET
}

public sealed record EquipmentRequest(
    int RefEquipmentId,
    string SerialNumber,
    EquipmentStatus Status,
    int? CreatedByStaffId)
{
    public string? Validate()
    {
        if (RefEquipmentId <= 0)
        {
            return "A valid equipment type is required.";
        }

        if (string.IsNullOrWhiteSpace(SerialNumber) || SerialNumber.Trim().Length > 80)
        {
            return "Serial number is required and must be 80 characters or fewer.";
        }

        return null;
    }

    public Equipment ToEquipment() => new()
    {
        RefEquipmentId = RefEquipmentId,
        SerialNumber = SerialNumber.Trim(),
        Status = Status,
        CreatedByStaffId = CreatedByStaffId
    };
}

public sealed record EquipmentResponse(
    int Id,
    int RefEquipmentId,
    string SerialNumber,
    EquipmentStatus Status,
    int? CreatedByStaffId);

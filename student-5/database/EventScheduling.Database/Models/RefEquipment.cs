namespace EventScheduling.Database.Models;

public sealed class RefEquipment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public void UpdateFrom(RefEquipmentRequest request)
    {
        Name = request.Name.Trim();
        Category = request.Category.Trim();
        Description = request.Description.Trim();
    }

    public RefEquipmentResponse ToResponse(int availableCount) =>
        new(Id, Name, Category, Description, availableCount);
}

public sealed record RefEquipmentRequest(
    string Name,
    string Category,
    string Description)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120)
        {
            return "Name is required and must be 120 characters or fewer.";
        }

        if (string.IsNullOrWhiteSpace(Category) || Category.Trim().Length > 80)
        {
            return "Category is required and must be 80 characters or fewer.";
        }

        return null;
    }

    public RefEquipment ToRefEquipment() => new()
    {
        Name = Name.Trim(),
        Category = Category.Trim(),
        Description = Description.Trim()
    };
}

public sealed record RefEquipmentResponse(
    int Id,
    string Name,
    string Category,
    string Description,
    int AvailableCount);

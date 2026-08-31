namespace EventScheduling.Database.Models;

public sealed class Equipment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int QuantityAvailable { get; set; }

    public void UpdateFrom(EquipmentRequest request)
    {
        Name = request.Name.Trim();
        Category = request.Category.Trim();
        Description = request.Description.Trim();
        QuantityAvailable = request.QuantityAvailable;
    }

    public EquipmentResponse ToResponse() => new(Id, Name, Category, Description, QuantityAvailable);
}

public sealed record EquipmentRequest(
    string Name,
    string Category,
    string Description,
    int QuantityAvailable)
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

        if (QuantityAvailable < 0)
        {
            return "Quantity available cannot be negative.";
        }

        return null;
    }

    public Equipment ToEquipment() => new()
    {
        Name = Name.Trim(),
        Category = Category.Trim(),
        Description = Description.Trim(),
        QuantityAvailable = QuantityAvailable
    };
}

public sealed record EquipmentResponse(
    int Id,
    string Name,
    string Category,
    string Description,
    int QuantityAvailable);

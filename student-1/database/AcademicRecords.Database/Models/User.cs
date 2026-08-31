namespace AcademicRecords.Database.Models;

public class User
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

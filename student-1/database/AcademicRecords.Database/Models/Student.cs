namespace AcademicRecords.Database.Models;

public sealed class Student : User
{
    public string StudentId { get; set; } = string.Empty;
    public string MailingAddress { get; set; } = string.Empty;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactPhone { get; set; } = string.Empty;
}
namespace AcademicRecords.Database.Models;

public sealed class Student : User
{
    public string StudentId { get; set; } = string.Empty;
    public string MailingAddress { get; set; } = string.Empty;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactPhone { get; set; } = string.Empty;
    public DateOnly? EnrollmentDate { get; set; }
    public string? Degree { get; set; }
    public string? Major { get; set; }
    public string? SubMajor { get; set; }
    public ICollection<Enrollment> Enrollments { get; } = [];
}
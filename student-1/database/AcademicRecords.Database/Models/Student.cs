namespace AcademicRecords.Database.Models;

public sealed class Student : User
{
    public string StudentId { get; set; } = string.Empty;
}
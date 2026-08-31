namespace CourseManagement.Database.Models;

public sealed class FacultyAssignment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public required string StaffName { get; set; }
    public required string StaffEmail { get; set; }
    public required string Role { get; set; }
    public required string TeachingPeriod { get; set; }

    public Course Course { get; set; } = null!;
}

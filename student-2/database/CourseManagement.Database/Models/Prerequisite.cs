namespace CourseManagement.Database.Models;

public sealed class Prerequisite
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int RequiredCourseId { get; set; }
    public string? MinimumGrade { get; set; }

    public Course Course { get; set; } = null!;
    public Course RequiredCourse { get; set; } = null!;
}

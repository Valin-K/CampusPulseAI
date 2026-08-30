namespace CourseManagement.Backend.Models;

public sealed class Course
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int CreditPoints { get; set; }
    public int Level { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SyllabusFileName { get; set; }

    public ICollection<Prerequisite> Prerequisites { get; set; } = [];
    public ICollection<Prerequisite> RequiredForCourses { get; set; } = [];
    public ICollection<FacultyAssignment> FacultyAssignments { get; set; } = [];
}

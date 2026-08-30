namespace AcademicRecords.Database.Models;

public sealed class Enrollment
{
    public int EnrollmentId { get; set; }
    public int? StudentId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public int? AcademicTermId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? GradeValue { get; set; }
    public decimal? Credits { get; set; }
    public DateOnly? GradeDate { get; set; }
    public bool IsFinalized { get; set; }

    public User? Student { get; set; }
    public AcademicTerm? AcademicTerm { get; set; }
}

namespace AcademicRecords.Database.Models;

public sealed class Enrollment
{
    public int EnrollmentId { get; set; }
    public int? StudentId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public int? AcademicTermId { get; set; }
    public int AcademicYear { get; set; }
    public string Status => IsFinalized
        ? EnrollmentStatusRules.GetFinalStatus(EnrollmentStatusRules.GetGradeType(CourseCode), GradeValue)
        : AcademicTerm is null
            ? EnrollmentStatusRules.Enrolled
            : EnrollmentStatusRules.GetStatus(
                EnrollmentStatusRules.GetSessionDates(AcademicYear, AcademicTerm.StartWeekOfYear, AcademicTerm.EndWeekOfYear).StartDate,
                EnrollmentStatusRules.GetSessionDates(AcademicYear, AcademicTerm.StartWeekOfYear, AcademicTerm.EndWeekOfYear).EndDate,
                DateOnly.FromDateTime(DateTime.UtcNow));
    public decimal? GradeValue { get; set; }
    public DateOnly? GradeDate { get; set; }
    public bool IsFinalized { get; set; }

    public Student? Student { get; set; }
    public AcademicTerm? AcademicTerm { get; set; }
}

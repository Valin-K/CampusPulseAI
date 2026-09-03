namespace AcademicRecords.Database.Models;

public sealed class Enrollment
{
    public int EnrollmentId { get; set; }
    public int? StudentId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public int? AcademicTermId { get; set; }
    public int AcademicYear { get; set; }
    public string Status
    {
        get
        {
            if (AcademicTerm is null)
            {
                return IsFinalized
                    ? EnrollmentStatusRules.GetFinalStatus(GradeValue)
                    : EnrollmentStatusRules.Enrolled;
            }

            var sessionDates = EnrollmentStatusRules.GetSessionDates(
                AcademicYear, AcademicTerm.StartWeekOfYear, AcademicTerm.EndWeekOfYear);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (sessionDates.StartDate > today)
            {
                return EnrollmentStatusRules.Enrolled;
            }

            if (sessionDates.EndDate >= today)
            {
                return EnrollmentStatusRules.InProgress;
            }

            return IsFinalized
                ? EnrollmentStatusRules.GetFinalStatus(GradeValue)
                : EnrollmentStatusRules.Completed;
        }
    }
    public decimal? GradeValue { get; set; }
    public DateOnly? GradeDate { get; set; }
    public bool IsFinalized { get; set; }

    public Student? Student { get; set; }
    public AcademicTerm? AcademicTerm { get; set; }
}

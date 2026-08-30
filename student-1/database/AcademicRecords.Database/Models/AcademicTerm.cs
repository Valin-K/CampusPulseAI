namespace AcademicRecords.Database.Models;

public sealed class AcademicTerm
{
    public int TermId { get; set; }
    public string TermCode { get; set; } = string.Empty;
    public string TermName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly? GradeLockDate { get; set; }
    public DateOnly? CensusDate { get; set; }
    public bool IsActive { get; set; }

    public ICollection<Enrollment> Enrollments { get; } = [];
}

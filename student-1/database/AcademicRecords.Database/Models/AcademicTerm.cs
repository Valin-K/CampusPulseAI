namespace AcademicRecords.Database.Models;

public sealed class AcademicTerm
{
    public int TermId { get; set; }
    public string TermCode { get; set; } = string.Empty;
    public string TermName { get; set; } = string.Empty;
    public int StartWeekOfYear { get; set; }
    public int EndWeekOfYear { get; set; }
    public bool IsActive { get; set; }

    public ICollection<Enrollment> Enrollments { get; } = new List<Enrollment>();
}

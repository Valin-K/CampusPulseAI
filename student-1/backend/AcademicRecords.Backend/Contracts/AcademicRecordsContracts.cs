namespace AcademicRecords.Backend.Contracts;

public sealed record CourseMetadataResponse(string Code, string Name, int CreditPoints, string GradeType);

public sealed record StudentStatisticsProfile
{
    public string StudentId { get; init; } = string.Empty;
    public List<StudentStatisticsEnrollment>? Enrollments { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DegreeCode { get; init; }
    public string? Degree { get; init; }
    public string? Status { get; init; }
    public string? Major { get; init; }
    public string? SubMajor { get; init; }
}

public sealed record StudentStatisticsEnrollment
{
    public string CourseCode { get; init; } = string.Empty;
    public string? CourseTitle { get; init; }
    public string? TermCode { get; init; }
    public string? TermName { get; init; }
    public int AcademicYear { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? GradeType { get; init; }
    public decimal? Credits { get; init; }
    public decimal? GradeValue { get; init; }
    public bool IsFinalized { get; init; }
}

public sealed record StudentStatisticResponse(
    string StudentId,
    string Statistic,
    decimal? Value,
    int GradedEnrollmentCount,
    decimal TotalCredits);

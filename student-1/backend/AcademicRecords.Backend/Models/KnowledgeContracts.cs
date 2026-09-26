using System.ComponentModel.DataAnnotations;

namespace AcademicRecords.Backend.Models;

public sealed class KnowledgeQuestionRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public required string Question { get; set; }

    public int? UserId { get; set; }
}

public sealed record StudentEnrollmentProfile(
    string StudentId,
    List<StudentEnrollmentResponse>? Enrollments);

public sealed record StudentEnrollmentResponse(
    int EnrollmentId,
    string CourseCode,
    string? TermCode,
    string? TermName,
    int AcademicYear,
    string Status,
    decimal? GradeValue,
    bool IsFinalized);

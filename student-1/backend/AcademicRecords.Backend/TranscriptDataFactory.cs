using AcademicRecords.Backend.Calculators;
using AcademicRecords.Backend.Contracts;
using AcademicRecords.Backend.Services;

namespace AcademicRecords.Backend;

public static class TranscriptDataFactory
{
    public static TranscriptData Create(StudentStatisticsProfile profile, DateTime printedAt)
    {
        var sourceEnrollments = profile.Enrollments ?? [];
        var subjects = sourceEnrollments.Select(CreateSubject).ToList();
        var gradedEnrollments = StudentStatisticsCalculator.ToGradedEnrollments(sourceEnrollments);
        var gpa = StudentStatisticsCalculator.CalculateGpa(gradedEnrollments).Value;
        var wam = StudentStatisticsCalculator.CalculateWam(gradedEnrollments).Value;

        return new TranscriptData(
            "Campus Pulse AI",
            "Online Student Record",
            printedAt,
            profile.StudentId,
            profile.FirstName,
            profile.LastName,
            profile.DegreeCode,
            profile.Degree,
            profile.Status ?? "Active",
            profile.Major,
            subjects.Where(subject => !subject.IsCompleted).ToList(),
            subjects.Where(subject => subject.IsCompleted).ToList(),
            gpa,
            wam,
            subjects.Sum(subject => subject.CreditsGained ?? 0),
            "This is an e-record produced by this student for their personal use from an online web accessible system. " +
            "This is not an official University academic transcript and it does not contain a full academic history for this " +
            "student. A copy of this document should not be accepted as an official University document.");
    }

    private static TranscriptSubject CreateSubject(StudentStatisticsEnrollment enrollment)
    {
        var isCompleted = enrollment.IsFinalized;
        var isNotMarked = string.Equals(enrollment.GradeType, "Pass/Fail", StringComparison.OrdinalIgnoreCase)
            || string.Equals(enrollment.GradeType, "Ungraded", StringComparison.OrdinalIgnoreCase);
        var creditsGained = isCompleted && IsCreditGained(enrollment.Status)
            ? enrollment.Credits
            : isCompleted ? 0 : null;

        return new TranscriptSubject(
            enrollment.CourseCode,
            enrollment.CourseTitle ?? enrollment.CourseCode,
            enrollment.AcademicYear,
            enrollment.TermCode ?? enrollment.TermName,
            isCompleted ? enrollment.Status.ToUpperInvariant() : "ENROLLED",
            CreateResult(enrollment),
            isNotMarked ? null : enrollment.GradeValue,
            enrollment.Credits,
            creditsGained,
            isCompleted);
    }

    private static string CreateResult(StudentStatisticsEnrollment enrollment)
    {
        if (!enrollment.IsFinalized)
        {
            return enrollment.Status.ToUpperInvariant();
        }

        if (string.Equals(enrollment.GradeType, "Pass/Fail", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(enrollment.Status, "Passed", StringComparison.OrdinalIgnoreCase)
                ? "PASS - NOT GRADED"
                : string.Equals(enrollment.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                    ? "FAIL - NOT GRADED"
                    : enrollment.Status.ToUpperInvariant();
        }

        if (string.Equals(enrollment.GradeType, "Ungraded", StringComparison.OrdinalIgnoreCase))
        {
            return "PASS - NOT GRADED";
        }

        return enrollment.GradeValue switch
        {
            >= 85 => "HIGH DISTINCTION",
            >= 75 => "DISTINCTION",
            >= 65 => "CREDIT",
            >= 50 => "PASS",
            _ => "FAIL"
        };
    }

    private static bool IsCreditGained(string status) =>
        string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);
}

public sealed record TranscriptSubject(
    string CourseCode,
    string CourseTitle,
    int AcademicYear,
    string? TermCode,
    string Status,
    string Result,
    decimal? Mark,
    decimal? CreditsEnrolled,
    decimal? CreditsGained,
    bool IsCompleted);
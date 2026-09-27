using AcademicRecords.Backend.Contracts;

namespace AcademicRecords.Backend;

public static class GraduationEligibilityCalculator
{
    public const decimal RequiredCreditPoints = 144m;

    public static GraduationEligibilityResult Calculate(StudentStatisticsProfile profile)
    {
        var enrollments = profile.Enrollments ?? [];
        var earnedCreditPoints = CreditPointsCalculator.Calculate(enrollments).TotalCreditPointsGained;

        var outstandingRequirements = new List<string>();
        if (earnedCreditPoints < RequiredCreditPoints)
        {
            outstandingRequirements.Add(
                $"Insufficient credit points: {earnedCreditPoints:0.##} of {RequiredCreditPoints:0.##} required.");
        }
        else
        {
            var activeCourses = enrollments
                .Where(IsActiveEnrollment)
                .Select(enrollment => enrollment.CourseCode)
                .Where(courseCode => !string.IsNullOrWhiteSpace(courseCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (activeCourses.Count > 0)
            {
                outstandingRequirements.Add(
                    $"Active courses must be completed: {string.Join(", ", activeCourses)}.");
            }
        }

        return new GraduationEligibilityResult(
            earnedCreditPoints >= RequiredCreditPoints && outstandingRequirements.Count == 0,
            earnedCreditPoints,
            RequiredCreditPoints,
            outstandingRequirements);
    }

    private static bool IsActiveEnrollment(StudentStatisticsEnrollment enrollment) =>
        string.Equals(enrollment.Status, "Enrolled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(enrollment.Status, "In Progress", StringComparison.OrdinalIgnoreCase);
}

public sealed record GraduationEligibilityResult(
    bool IsEligible,
    decimal EarnedCreditPoints,
    decimal RequiredCreditPoints,
    IReadOnlyList<string> OutstandingRequirements);

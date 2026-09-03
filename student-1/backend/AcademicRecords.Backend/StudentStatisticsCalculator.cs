namespace AcademicRecords.Backend;

public static class StudentStatisticsCalculator
{
    public static IReadOnlyList<GradedEnrollment> ToGradedEnrollments(
        IEnumerable<StudentStatisticsEnrollment>? enrollments) =>
        (enrollments ?? [])
            .Select(enrollment => new GradedEnrollment(enrollment.GradeValue, enrollment.Credits, enrollment.IsFinalized))
            .ToList();

    public static StudentStatistic CalculateWam(IEnumerable<GradedEnrollment>? enrollments) =>
        Calculate(enrollments, static enrollment => enrollment.Mark!.Value);

    public static StudentStatistic CalculateGpa(IEnumerable<GradedEnrollment>? enrollments) =>
        Calculate(enrollments, static enrollment => GradePoints(enrollment.Mark!.Value));

    public static decimal GradePoints(decimal mark) => mark switch
    {
        >= 85 => 7,
        >= 75 => 6,
        >= 65 => 5,
        >= 50 => 4,
        _ => 0
    };

    private static StudentStatistic Calculate(
        IEnumerable<GradedEnrollment>? enrollments,
        Func<GradedEnrollment, decimal> valueSelector)
    {
        var gradedEnrollments = (enrollments ?? [])
            .Where(enrollment => enrollment.IsFinalized && enrollment.Mark is not null && enrollment.Credits is > 0)
            .ToList();
        var totalCredits = gradedEnrollments.Sum(enrollment => enrollment.Credits!.Value);

        return new StudentStatistic(
            gradedEnrollments.Count,
            totalCredits,
            totalCredits == 0
                ? null
                : gradedEnrollments.Sum(enrollment => valueSelector(enrollment) * enrollment.Credits!.Value) / totalCredits);
    }
}

public sealed record GradedEnrollment(decimal? Mark, decimal? Credits, bool IsFinalized);

public sealed record StudentStatistic(int GradedEnrollmentCount, decimal TotalCredits, decimal? Value);

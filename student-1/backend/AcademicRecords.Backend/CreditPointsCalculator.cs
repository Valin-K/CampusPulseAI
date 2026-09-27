using AcademicRecords.Backend.Contracts;

namespace AcademicRecords.Backend;

public static class CreditPointsCalculator
{
    public static CreditPointsSummary Calculate(IEnumerable<StudentStatisticsEnrollment>? enrollments)
    {
        var creditPointsGainedPerYear = (enrollments ?? [])
            .Where(IsCreditEarningEnrollment)
            .GroupBy(enrollment => enrollment.AcademicYear)
            .OrderBy(group => group.Key)
            .Select(group => new AnnualCreditPoints(
                group.Key,
                group.Sum(enrollment => enrollment.Credits!.Value)))
            .ToList();

        return new CreditPointsSummary(
            creditPointsGainedPerYear.Sum(year => year.CreditPointsGained),
            creditPointsGainedPerYear);
    }

    private static bool IsCreditEarningEnrollment(StudentStatisticsEnrollment enrollment) =>
        enrollment.IsFinalized &&
        (string.Equals(enrollment.Status, "Passed", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(enrollment.Status, "Completed", StringComparison.OrdinalIgnoreCase)) &&
        enrollment.Credits is > 0;
}

public sealed record CreditPointsSummary(
    decimal TotalCreditPointsGained,
    IReadOnlyList<AnnualCreditPoints> CreditPointsGainedPerYear);

public sealed record AnnualCreditPoints(int AcademicYear, decimal CreditPointsGained);

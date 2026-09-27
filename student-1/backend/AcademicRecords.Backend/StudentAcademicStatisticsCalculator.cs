using AcademicRecords.Backend.Contracts;

namespace AcademicRecords.Backend;

public static class StudentAcademicStatisticsCalculator
{
    public static StudentAcademicStatisticsResponse Calculate(StudentStatisticsProfile profile)
    {
        var gradedEnrollments = StudentStatisticsCalculator.ToGradedEnrollments(profile.Enrollments);
        var wam = StudentStatisticsCalculator.CalculateWam(gradedEnrollments);
        var gpa = StudentStatisticsCalculator.CalculateGpa(gradedEnrollments);
        var creditPoints = CreditPointsCalculator.Calculate(profile.Enrollments);

        return new StudentAcademicStatisticsResponse(
            profile.StudentId,
            new StudentStatisticResponse(profile.StudentId, "WAM", wam.Value, wam.GradedEnrollmentCount, wam.TotalCredits),
            new StudentStatisticResponse(profile.StudentId, "GPA", gpa.Value, gpa.GradedEnrollmentCount, gpa.TotalCredits),
            creditPoints.TotalCreditPointsGained,
            creditPoints.CreditPointsGainedPerYear);
    }
}

public sealed record StudentAcademicStatisticsResponse(
    string StudentId,
    StudentStatisticResponse Wam,
    StudentStatisticResponse Gpa,
    decimal TotalCreditPointsGained,
    IReadOnlyList<AnnualCreditPoints> CreditPointsGainedPerYear);

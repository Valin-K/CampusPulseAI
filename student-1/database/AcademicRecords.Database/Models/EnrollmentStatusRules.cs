using System.Globalization;

namespace AcademicRecords.Database.Models;

public static class EnrollmentStatusRules
{
    public const string Enrolled = "Enrolled";
    public const string InProgress = "In Progress";
    public const string Completed = "Completed";

    public static (DateOnly StartDate, DateOnly EndDate) GetSessionDates(int academicYear, int startWeekOfYear, int endWeekOfYear)
    {
        var startDate = DateOnly.FromDateTime(ISOWeek.ToDateTime(academicYear, startWeekOfYear, DayOfWeek.Monday));
        var endYear = endWeekOfYear < startWeekOfYear ? academicYear + 1 : academicYear;
        var endDate = DateOnly.FromDateTime(ISOWeek.ToDateTime(endYear, endWeekOfYear, DayOfWeek.Sunday));
        return (startDate, endDate);
    }

    public static string GetStatus(DateOnly startDate, DateOnly endDate, DateOnly today) =>
        endDate < today ? Completed : startDate > today ? Enrolled : InProgress;
}

using System.Globalization;

namespace AcademicRecords.Database.Models;

public static class EnrollmentStatusRules
{
    public const string Enrolled = "Enrolled";
    public const string InProgress = "In Progress";
    public const string Completed = "Completed";
    public const string Passed = "Passed";
    public const string Failed = "Failed";

    public static class EnrollmentGradeTypes
    {
        public const string Mark = "Mark";
        public const string PassFail = "Pass/Fail";
        public const string Ungraded = "Ungraded";
    }

    public static (DateOnly StartDate, DateOnly EndDate) GetSessionDates(int academicYear, int startWeekOfYear, int endWeekOfYear)
    {
        var startDate = DateOnly.FromDateTime(ISOWeek.ToDateTime(academicYear, startWeekOfYear, DayOfWeek.Monday));
        var endYear = endWeekOfYear < startWeekOfYear ? academicYear + 1 : academicYear;
        var endDate = DateOnly.FromDateTime(ISOWeek.ToDateTime(endYear, endWeekOfYear, DayOfWeek.Sunday));
        return (startDate, endDate);
    }

    public static string GetStatus(DateOnly startDate, DateOnly endDate, DateOnly today) =>
        endDate < today ? Completed : startDate > today ? Enrolled : InProgress;

    public static string GetFinalStatus(string gradeType, decimal? gradeValue) =>
        gradeType switch
        {
            EnrollmentGradeTypes.Mark when gradeValue >= 50m => Passed,
            EnrollmentGradeTypes.Mark when gradeValue is not null => Failed,
            EnrollmentGradeTypes.PassFail when gradeValue >= 50m => Passed,
            EnrollmentGradeTypes.PassFail when gradeValue is not null => Failed,
            EnrollmentGradeTypes.Ungraded => Completed,
            _ => Completed
        };

    public static string GetFinalStatus(decimal? gradeValue) =>
        gradeValue switch
        {
            >= 50m => Passed,
            not null => Failed,
            _ => Completed
        };
}

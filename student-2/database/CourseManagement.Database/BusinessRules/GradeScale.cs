namespace CourseManagement.Backend.BusinessRules;

public static class GradeScale
{
    static readonly IReadOnlyDictionary<string, (string CanonicalGrade, int Rank)> Grades =
        new Dictionary<string, (string CanonicalGrade, int Rank)>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Fail"] = ("Fail", 0),
            ["F"] = ("Fail", 0),
            ["Pass"] = ("Pass", 1),
            ["P"] = ("Pass", 1),
            ["Credit"] = ("Credit", 2),
            ["C"] = ("Credit", 2),
            ["Distinction"] = ("Distinction", 3),
            ["D"] = ("Distinction", 3),
            ["High Distinction"] = ("High Distinction", 4),
            ["HD"] = ("High Distinction", 4)
        };

    public static bool TryGetRank(string grade, out int rank)
    {
        if (TryNormalize(grade, out _, out rank))
        {
            return true;
        }

        rank = default;
        return false;
    }

    public static bool TryNormalize(
        string grade,
        out string canonicalGrade,
        out int rank)
    {
        if (Grades.TryGetValue(grade.Trim(), out var value))
        {
            canonicalGrade = value.CanonicalGrade;
            rank = value.Rank;
            return true;
        }

        canonicalGrade = string.Empty;
        rank = default;
        return false;
    }
}

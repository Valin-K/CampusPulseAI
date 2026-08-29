namespace CourseManagement.Backend.BusinessRules;

public static class GradeScale
{
    static readonly IReadOnlyDictionary<string, int> GradeRanks =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fail"] = 0,
            ["F"] = 0,
            ["Pass"] = 1,
            ["P"] = 1,
            ["Credit"] = 2,
            ["C"] = 2,
            ["Distinction"] = 3,
            ["D"] = 3,
            ["High Distinction"] = 4,
            ["HD"] = 4
        };

    public static bool TryGetRank(string grade, out int rank)
    {
        return GradeRanks.TryGetValue(grade.Trim(), out rank);
    }
}

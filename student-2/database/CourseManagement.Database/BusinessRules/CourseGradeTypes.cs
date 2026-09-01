namespace CourseManagement.Backend.BusinessRules;

public static class CourseGradeTypes
{
    public const string Mark = "Mark";
    public const string PassFail = "Pass/Fail";
    public const string Ungraded = "Ungraded";

    static readonly IReadOnlyDictionary<string, string> Values =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Mark] = Mark,
            [PassFail] = PassFail,
            [Ungraded] = Ungraded
        };

    public static bool TryNormalize(string? gradeType, out string canonicalGradeType)
    {
        if (!string.IsNullOrWhiteSpace(gradeType)
            && Values.TryGetValue(gradeType.Trim(), out var value))
        {
            canonicalGradeType = value;
            return true;
        }

        canonicalGradeType = string.Empty;
        return false;
    }
}

namespace CourseManagement.Backend.Contracts.CourseAccess;

public static class CourseAccessRoles
{
    public const string Student = "Student";
    public const string CourseAdministrator = "CourseAdministrator";
}

public sealed record CourseAccessUserResponse(
    int UserId,
    string ExternalId,
    string DisplayName,
    string Email,
    string SourceRole,
    string CourseRole);

namespace CourseManagement.Frontend.Models;

public static class CourseAccessRoles
{
    public const string Student = "Student";
    public const string CourseAdministrator = "CourseAdministrator";
}

public sealed record CourseAccessUser(
    int UserId,
    string ExternalId,
    string DisplayName,
    string Email,
    string SourceRole,
    string CourseRole);

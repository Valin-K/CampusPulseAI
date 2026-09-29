using McpServer.Contracts;
using McpServer.Tools;

namespace McpServer.Tools.CourseManagement;

public sealed class ListCoursesTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.list_courses",
        "List all courses in the Course Management catalogue, including their active status and syllabus availability.",
        "Course Management",
        new Dictionary<string, string>())
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        courseManagement.ListCoursesAsync(cancellationToken);
}

public sealed class GetCourseDetailsTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.get_details",
        "Get one course with its prerequisites, faculty assignments and syllabus metadata.",
        "Course Management",
        CourseToolSchemas.Identifier())
{
    protected override async Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var courseId = await courseManagement.ResolveCourseIdAsync(
            request.Arguments,
            cancellationToken);
        return await courseManagement.GetCourseDetailsAsync(
            courseId,
            cancellationToken);
    }
}

public sealed class GetCoursePrerequisitesTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.get_prerequisites",
        "Get the prerequisite requirements for one course.",
        "Course Management",
        CourseToolSchemas.Identifier())
{
    protected override async Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var courseId = await courseManagement.ResolveCourseIdAsync(
            request.Arguments,
            cancellationToken);
        return await courseManagement.GetCoursePrerequisitesAsync(
            courseId,
            cancellationToken);
    }
}

public sealed class GetFacultyAssignmentsTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.get_faculty_assignments",
        "Get the faculty members assigned to one course.",
        "Course Management",
        CourseToolSchemas.Identifier())
{
    protected override async Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var courseId = await courseManagement.ResolveCourseIdAsync(
            request.Arguments,
            cancellationToken);
        return await courseManagement.GetFacultyAssignmentsAsync(
            courseId,
            cancellationToken);
    }
}

public sealed class CheckPrerequisiteEligibilityTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.check_prerequisite_eligibility",
        "Check whether completed courses and grades satisfy a target course's prerequisite requirements.",
        "Course Management",
        CourseToolSchemas.Identifier(
            ("completedCourses",
                "Required array of objects containing courseCode and grade.")))
{
    protected override async Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var courseId = await courseManagement.ResolveCourseIdAsync(
            request.Arguments,
            cancellationToken);
        return await courseManagement.CheckPrerequisiteEligibilityAsync(
            courseId,
            courseManagement.RequiredCompletedCourses(request.Arguments),
            cancellationToken);
    }
}

internal static class CourseToolSchemas
{
    public static Dictionary<string, string> Identifier(
        params (string Name, string Description)[] additionalArguments)
    {
        var schema = new Dictionary<string, string>
        {
            ["courseId"] =
                "Optional positive Course database ID. Provide exactly one Course identifier.",
            ["courseCode"] =
                "Optional exact Course code. Provide exactly one Course identifier.",
            ["courseName"] =
                "Optional exact Course name. Provide exactly one Course identifier."
        };

        foreach (var (name, description) in additionalArguments)
        {
            schema[name] = description;
        }

        return schema;
    }
}

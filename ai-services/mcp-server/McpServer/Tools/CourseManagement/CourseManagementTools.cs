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
        new Dictionary<string, string>
        {
            ["courseId"] = "Required positive course database ID."
        })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        courseManagement.GetCourseDetailsAsync(
            courseManagement.RequiredCourseId(request.Arguments),
            cancellationToken);
}

public sealed class GetCoursePrerequisitesTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.get_prerequisites",
        "Get the prerequisite requirements for one course.",
        "Course Management",
        new Dictionary<string, string>
        {
                ["courseId"] = "Required positive course database ID."
        })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        courseManagement.GetCoursePrerequisitesAsync(
            courseManagement.RequiredCourseId(request.Arguments),
            cancellationToken);
}

public sealed class GetFacultyAssignmentsTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.get_faculty_assignments",
        "Get the faculty members assigned to one course.",
        "Course Management",
        new Dictionary<string, string>
        {
            ["courseId"] = "Required positive course database ID."
        })  
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        courseManagement.GetFacultyAssignmentsAsync(
            courseManagement.RequiredCourseId(request.Arguments),
            cancellationToken);
}

public sealed class CheckPrerequisiteEligibilityTool(CourseManagementApiClient courseManagement)
    : McpTool(
        "course.check_prerequisite_eligibility",
        "Check whether completed courses and grades satisfy a target course's prerequisite requirements.",
        "Course Management",
        new Dictionary<string, string>
            {
                ["courseId"] = "Required positive target course database ID.",
                ["completedCourses"] = "Required array of objects containing courseCode and grade."
            })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        courseManagement.CheckPrerequisiteEligibilityAsync(
            courseManagement.RequiredCourseId(request.Arguments),
            courseManagement.RequiredCompletedCourses(request.Arguments),
            cancellationToken);
}

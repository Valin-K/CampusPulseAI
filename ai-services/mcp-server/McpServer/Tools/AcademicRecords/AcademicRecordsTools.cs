using McpServer.Contracts;
using McpServer.Tools;

namespace McpServer.Tools.AcademicRecords;

public sealed class CalculateStudentAcademicStatisticsTool(
    AcademicRecordsApiClient academicRecords) : McpTool(
        "calculate_student_academic_statistics",
        "Return a student's weighted average mark (WAM), grade point average (GPA), and total credit points gained by academic year.",
        "Academic Records",
        new Dictionary<string, string>
        {
            ["student_id"] = "Required student identifier, for example STU123"
        })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        academicRecords.GetAcademicStatisticsAsync(
            academicRecords.RequiredStudentId(request.Arguments),
            cancellationToken);
}

public sealed class GetStudentEnrollmentsTool(
    AcademicRecordsApiClient academicRecords) : McpTool(
        "get_student_enrollments",
        "Return the course, term, status, grade, and finalization details for a student.",
        "Academic Records",
        new Dictionary<string, string>
        {
            ["user_id"] = "Required numeric user ID for the student"
        })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        academicRecords.GetEnrollmentsAsync(
            academicRecords.RequiredUserId(request.Arguments),
            cancellationToken);
}

public sealed class StudentsBySubjectTool(
    AcademicRecordsApiClient academicRecords) : McpTool(
    "students_by_subject",
    "Return the number of students enrolled in a course or subject.",
    "Academic Records",
    new Dictionary<string, string>
    {
        ["subject_code"] = "Required course or subject code, for example CS101"
    })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        academicRecords.GetStudentsBySubjectAsync(
            academicRecords.RequiredSubjectCode(request.Arguments),
            cancellationToken);
}

public sealed class StudentCountTool(
    AcademicRecordsApiClient academicRecords) : McpTool(
    "student_count",
    "Return the number of student profiles in Academic Records.",
    "Academic Records",
    new Dictionary<string, string>())
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        academicRecords.GetStudentCountAsync(cancellationToken);
}
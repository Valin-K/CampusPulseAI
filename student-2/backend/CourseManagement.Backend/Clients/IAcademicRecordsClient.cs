namespace CourseManagement.Backend.Clients;

public interface IAcademicRecordsClient
{
    Task<IReadOnlyList<AcademicRecordsUserResponse>> GetUsersAsync(
        CancellationToken cancellationToken);

    Task<AcademicRecordsStudentResponse> GetStudentAsync(
        string studentId,
        CancellationToken cancellationToken);

    Task CreateEnrollmentAsync(
        string studentId,
        AcademicRecordsEnrollmentRequest request,
        CancellationToken cancellationToken);
}

public sealed record AcademicRecordsUserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

public sealed record AcademicRecordsStudentResponse(
    int UserId,
    string StudentId,
    List<AcademicRecordsEnrollmentResponse> Enrollments);

public sealed record AcademicRecordsEnrollmentResponse(
    int EnrollmentId,
    string CourseCode,
    int? AcademicTermId,
    string Status,
    decimal? Credits,
    bool IsFinalized);

public sealed record AcademicRecordsEnrollmentRequest(
    string CourseCode,
    int AcademicTermId,
    int AcademicYear);

public sealed class AcademicRecordsResponseException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

namespace CourseManagement.Backend.Clients;

public interface IAcademicRecordsClient
{
    Task<IReadOnlyList<AcademicRecordsUserResponse>> GetUsersAsync(
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

public sealed class AcademicRecordsResponseException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

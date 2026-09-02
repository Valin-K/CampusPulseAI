namespace EventScheduling.Backend.Clients;

public interface IAcademicRecordsClient
{
    Task<IReadOnlyList<AcademicRecordsUser>> GetUsersAsync(CancellationToken cancellationToken);
}

// Shape of a row from Academic Records (student-1) GET /api/users.
public sealed record AcademicRecordsUser(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

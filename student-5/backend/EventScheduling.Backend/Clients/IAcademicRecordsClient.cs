namespace EventScheduling.Backend.Clients;

public interface IAcademicRecordsClient
{
    Task<IReadOnlyList<AcademicRecordsUser>> GetUsersAsync(CancellationToken cancellationToken);
}

public sealed record AcademicRecordsUser(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

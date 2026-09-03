namespace FinancialAidBilling.Frontend.Models;

public sealed record StudentProfile(
    int UserId,
    string StudentId,
    string FirstName,
    string LastName,
    string Email,
    DateOnly? EnrollmentDate,
    string? Degree,
    string? Major,
    string? SubMajor)
{
    public string FullName => $"{FirstName} {LastName}";

    public string Initials => string.Concat(
        FirstName.Take(1),
        LastName.Take(1)).ToUpperInvariant();

    public int YearLevel => EnrollmentDate is null
        ? 1
        : Math.Max(1, DateTime.UtcNow.Year - EnrollmentDate.Value.Year + 1);

    public string StudyDescriptor => string.IsNullOrWhiteSpace(Major)
        ? "Undeclared"
        : Major;
}

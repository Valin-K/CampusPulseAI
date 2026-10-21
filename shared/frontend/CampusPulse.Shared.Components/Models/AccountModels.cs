using System.ComponentModel.DataAnnotations;

namespace CampusPulse.Shared.Components.Models;

public sealed record CampusAccountUser(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string ExternalId => StudentId ?? StaffId ?? string.Empty;
    public string Initials => string.Concat(
        FirstName.FirstOrDefault().ToString(),
        LastName.FirstOrDefault().ToString()).ToUpperInvariant();
}

public sealed class CampusUserProfile
{
    public int UserId { get; set; }
    public string? StudentId { get; set; }
    public string? StaffId { get; set; }
    [Required]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    public string LastName { get; set; } = string.Empty;
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    [Required]
    public DateOnly? DateOfBirth { get; set; }
    public string? PhoneNumber { get; set; }
    public string? MailingAddress { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public DateOnly? EnrollmentDate { get; set; }
    public string? Degree { get; set; }
    public string? Major { get; set; }
    public string? SubMajor { get; set; }
}

public sealed record CampusStudentRegistration(
    string FirstName,
    string LastName,
    string Email,
    DateOnly? DateOfBirth,
    string Password,
    string ConfirmPassword,
    string MailingAddress,
    string EmergencyContactName,
    string EmergencyContactPhone,
    string? PhoneNumber = null,
    string? Degree = null,
    string? Major = null);

public sealed record CampusStaffRegistration(
    string FirstName,
    string LastName,
    string Email,
    DateOnly? DateOfBirth,
    string Password,
    string ConfirmPassword,
    string? PhoneNumber = null);

public sealed record CampusCreatedUser(string StudentId, int UserId);

public sealed record CampusCreatedStaff(string StaffId, int UserId);

public sealed record CampusUpdateStudentProfile(
    string FirstName,
    string LastName,
    string Email,
    DateOnly? DateOfBirth,
    string MailingAddress,
    string EmergencyContactName,
    string EmergencyContactPhone,
    string? PhoneNumber,
    string? Degree,
    string? Major,
    string? SubMajor);

public sealed record CampusUpdateStaffProfile(
    string FirstName,
    string LastName,
    string Email,
    DateOnly? DateOfBirth,
    string? PhoneNumber);

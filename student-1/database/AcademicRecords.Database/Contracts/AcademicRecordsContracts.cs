namespace AcademicRecords.Database.Contracts;

public sealed record EnrollStudentRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string MailingAddress, string EmergencyContactName, string EmergencyContactPhone, string? PhoneNumber = null,
    DateOnly? EnrollmentDate = null, string? Degree = null, string? Major = null, string? SubMajor = null,
    int? AcademicTermId = null, string? CourseCode = null, int? AcademicYear = null);

public sealed record AddEnrollmentRequest(string CourseCode, int AcademicTermId, int? AcademicYear = null);

public sealed record GradeEntryRequest(decimal? GradeValue);

public sealed record EnrollStaffRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string? PhoneNumber = null);

public sealed record StudentProfileResponse(int UserId, string StudentId, string FirstName, string LastName, string Email,
    DateOnly? DateOfBirth, string? PhoneNumber, string MailingAddress, string EmergencyContactName, string EmergencyContactPhone,
    DateOnly? EnrollmentDate, string? Degree, string? Major, string? SubMajor, List<TermEnrollmentResponse> Enrollments,
    DateOnly? GraduationDate = null);

public sealed record StaffProfileResponse(int UserId, string StaffId, string FirstName, string LastName, string Email,
    DateOnly? DateOfBirth, string? PhoneNumber);

public sealed record TermEnrollmentResponse(int EnrollmentId, string CourseCode, int? AcademicTermId, string? TermCode,
    string? TermName, int StartWeekOfYear, int EndWeekOfYear, int AcademicYear, string Status, string? GradeType,
    decimal? GradeValue, bool IsFinalized);

public sealed record UpdateStudentRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string MailingAddress, string EmergencyContactName, string EmergencyContactPhone, string? PhoneNumber = null,
    string? Degree = null, string? Major = null, string? SubMajor = null);

public sealed record UpdateStaffRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string? PhoneNumber = null);

public sealed record GraduationUpdateRequest(DateOnly GraduationDate);

public sealed record GraduationResponse(string StudentId, DateOnly GraduationDate);

public sealed record UserResponse(int UserId, string FirstName, string LastName, string Email, string Role,
    string? StudentId, string? StaffId);

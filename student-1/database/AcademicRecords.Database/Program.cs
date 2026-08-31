using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("AcademicRecords")
    ?? "Data Source=/app/data/academic-records.db";

builder.Services.AddDbContext<AcademicRecordsDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AcademicRecordsDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.MapGet("/api/users", async (AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
    await database.Users
        .AsNoTracking()
        .OrderBy(user => user.LastName)
        .ThenBy(user => user.FirstName)
        .Select(user => new UserResponse(
            user.UserId,
            user.FirstName,
            user.LastName,
            user.Email,
            user is Student ? "Student" : "Staff",
            user is Student ? ((Student)user).StudentId : null,
            user is Staff ? ((Staff)user).StaffId : null))
        .ToListAsync(cancellationToken));

app.MapPost("/api/students/enroll", async (EnrollStudentRequest request, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
        string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null ||
        string.IsNullOrWhiteSpace(request.MailingAddress) || string.IsNullOrWhiteSpace(request.EmergencyContactName) ||
        string.IsNullOrWhiteSpace(request.EmergencyContactPhone))
    {
        return Results.BadRequest("First name, last name, email, date of birth, mailing address, and emergency contact details are required.");
    }

    if (request.AcademicTermId is not null && string.IsNullOrWhiteSpace(request.CourseCode))
    {
        return Results.BadRequest("Course code is required when an academic term is supplied.");
    }

    if (request.AcademicTermId is not null && !await database.AcademicTerms.AnyAsync(term => term.TermId == request.AcademicTermId, cancellationToken))
    {
        return Results.NotFound("Academic term was not found.");
    }

    var student = new Student
    {
        StudentId = CreateStudentId(),
        FirstName = request.FirstName.Trim(),
        LastName = request.LastName.Trim(),
        Email = request.Email.Trim(),
        DateOfBirth = request.DateOfBirth,
        EnrollmentDate = request.EnrollmentDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
        Status = "ACT",
        PhoneNumber = request.PhoneNumber?.Trim(),
        MailingAddress = request.MailingAddress.Trim(),
        EmergencyContactName = request.EmergencyContactName.Trim(),
        EmergencyContactPhone = request.EmergencyContactPhone.Trim(),
        Degree = request.Degree?.Trim(),
        Major = request.Major?.Trim(),
        SubMajor = request.SubMajor?.Trim()
    };
    database.Add(student);

    if (request.AcademicTermId is not null)
    {
        database.Enrollments.Add(new Enrollment
        {
            Student = student,
            CourseCode = request.CourseCode!.Trim(),
            AcademicTermId = request.AcademicTermId,
            AcademicYear = request.AcademicYear ?? DateTime.UtcNow.Year,
            Status = "Enrolled"
        });
    }

    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/students/{student.StudentId}", new { student.StudentId, student.UserId });
});

app.MapPost("/api/students/{studentId}/enrollments", async (string studentId, AddEnrollmentRequest request, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var student = await database.Set<Student>().SingleOrDefaultAsync(value => value.StudentId == studentId, cancellationToken);
    if (student is null)
    {
        return Results.NotFound("Student was not found.");
    }

    if (string.IsNullOrWhiteSpace(request.CourseCode) || request.AcademicTermId <= 0)
    {
        return Results.BadRequest("Course code and academic term are required.");
    }

    if (!await database.AcademicTerms.AnyAsync(term => term.TermId == request.AcademicTermId, cancellationToken))
    {
        return Results.NotFound("Academic term was not found.");
    }

    var enrollment = new Enrollment
    {
        StudentId = student.UserId,
        CourseCode = request.CourseCode.Trim(),
        AcademicTermId = request.AcademicTermId,
        AcademicYear = request.AcademicYear ?? DateTime.UtcNow.Year,
        Status = "Enrolled"
    };
    database.Enrollments.Add(enrollment);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/students/{studentId}/enrollments/{enrollment.EnrollmentId}", enrollment.EnrollmentId);
});

app.MapGet("/api/students/{studentId}", async (string studentId, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var student = await database.Set<Student>().AsNoTracking()
        .Where(value => value.StudentId == studentId)
        .Select(value => new StudentProfileResponse(value.UserId, value.StudentId, value.FirstName, value.LastName, value.Email,
            value.DateOfBirth, value.PhoneNumber, value.MailingAddress, value.EmergencyContactName, value.EmergencyContactPhone,
            value.EnrollmentDate, value.Degree, value.Major, value.SubMajor,
            value.Enrollments.Select(enrollment => new TermEnrollmentResponse(enrollment.EnrollmentId, enrollment.CourseCode,
                enrollment.AcademicTermId, enrollment.AcademicYear, enrollment.Status)).ToList()))
        .SingleOrDefaultAsync(cancellationToken);
    return student is null ? Results.NotFound() : Results.Ok(student);
});

app.MapPut("/api/students/{studentId}", async (string studentId, UpdateStudentRequest request, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var student = await database.Students.SingleOrDefaultAsync(value => value.StudentId == studentId, cancellationToken);
    if (student is null)
    {
        return Results.NotFound("Student was not found.");
    }

    if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
        string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null ||
        string.IsNullOrWhiteSpace(request.MailingAddress) || string.IsNullOrWhiteSpace(request.EmergencyContactName) ||
        string.IsNullOrWhiteSpace(request.EmergencyContactPhone))
    {
        return Results.BadRequest("First name, last name, email, date of birth, mailing address, and emergency contact details are required.");
    }

    if (!IsValidEmail(request.Email))
    {
        return Results.BadRequest("Email address is not valid.");
    }

    if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !IsValidPhoneNumber(request.PhoneNumber))
    {
        return Results.BadRequest("Phone number is not valid. Use format: 0400000000 or +61400000000");
    }

    if (!IsValidPhoneNumber(request.EmergencyContactPhone))
    {
        return Results.BadRequest("Emergency contact phone number is not valid. Use format: 0400000000 or +61400000000");
    }

    student.FirstName = request.FirstName.Trim();
    student.LastName = request.LastName.Trim();
    student.Email = request.Email.Trim();
    student.DateOfBirth = request.DateOfBirth;
    student.PhoneNumber = request.PhoneNumber?.Trim();
    student.MailingAddress = request.MailingAddress.Trim();
    student.EmergencyContactName = request.EmergencyContactName.Trim();
    student.EmergencyContactPhone = request.EmergencyContactPhone.Trim();
    student.Degree = request.Degree?.Trim();
    student.Major = request.Major?.Trim();
    student.SubMajor = request.SubMajor?.Trim();

    await database.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.Run();

static string CreateStudentId() => $"STU{Guid.NewGuid():N}"[..13].ToUpperInvariant();

static bool IsValidEmail(string email)
{
    try
    {
        var addr = new System.Net.Mail.MailAddress(email);
        return addr.Address == email;
    }
    catch
    {
        return false;
    }
}

static bool IsValidPhoneNumber(string phoneNumber)
{
    var cleaned = new string(phoneNumber.Where(char.IsDigit).ToArray());
    if (phoneNumber.StartsWith("+61"))
    {
        return cleaned.Length == 11 && cleaned.StartsWith("61");
    }
    return cleaned.Length == 10 && cleaned.StartsWith("0");
}

public sealed record EnrollStudentRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string MailingAddress, string EmergencyContactName, string EmergencyContactPhone, string? PhoneNumber = null,
    DateOnly? EnrollmentDate = null, string? Degree = null, string? Major = null, string? SubMajor = null,
    int? AcademicTermId = null, string? CourseCode = null, int? AcademicYear = null);

public sealed record AddEnrollmentRequest(string CourseCode, int AcademicTermId, int? AcademicYear = null);

public sealed record StudentProfileResponse(int UserId, string StudentId, string FirstName, string LastName, string Email,
    DateOnly? DateOfBirth, string? PhoneNumber, string MailingAddress, string EmergencyContactName, string EmergencyContactPhone,
    DateOnly? EnrollmentDate, string? Degree, string? Major, string? SubMajor, List<TermEnrollmentResponse> Enrollments);

public sealed record TermEnrollmentResponse(int EnrollmentId, string CourseCode, int? AcademicTermId, int AcademicYear, string Status);

public sealed record UpdateStudentRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string MailingAddress, string EmergencyContactName, string EmergencyContactPhone, string? PhoneNumber = null,
    string? Degree = null, string? Major = null, string? SubMajor = null);

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

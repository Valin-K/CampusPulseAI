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
    await AcademicRecordsSeedData.InitializeAsync(database);
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

app.MapGet("/api/students/by-user/{userId:int}", async (int userId, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var value = await database.Students.AsNoTracking()
        .Include(student => student.Enrollments).ThenInclude(enrollment => enrollment.AcademicTerm)
        .SingleOrDefaultAsync(student => student.UserId == userId, cancellationToken);
    var student = value is null ? null : CreateStudentProfileResponse(value);

    return student is null ? Results.NotFound() : Results.Ok(student);
});

app.MapGet("/api/staff/by-user/{userId:int}", async (int userId, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var staff = await database.Set<Staff>().AsNoTracking()
        .Where(value => value.UserId == userId)
        .Select(value => new StaffProfileResponse(value.UserId, value.StaffId, value.FirstName, value.LastName,
            value.Email, value.DateOfBirth, value.PhoneNumber))
        .SingleOrDefaultAsync(cancellationToken);

    return staff is null ? Results.NotFound() : Results.Ok(staff);
});

app.MapGet("/api/staff", async (AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
    await database.Set<Staff>().AsNoTracking()
        .OrderBy(value => value.LastName)
        .ThenBy(value => value.FirstName)
        .Select(value => new StaffProfileResponse(value.UserId, value.StaffId, value.FirstName, value.LastName,
            value.Email, value.DateOfBirth, value.PhoneNumber))
        .ToListAsync(cancellationToken));

app.MapPost("/api/staff/enroll", async (EnrollStaffRequest request, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
        string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null)
    {
        return Results.BadRequest("First name, last name, email, and date of birth are required.");
    }

    if (!IsValidEmail(request.Email))
    {
        return Results.BadRequest("Email address is not valid.");
    }

    if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !IsValidPhoneNumber(request.PhoneNumber))
    {
        return Results.BadRequest("Phone number is not valid. Use format: 0400000000 or +61400000000");
    }

    var staff = new Staff
    {
        StaffId = await CreateStaffIdAsync(database, cancellationToken),
        FirstName = request.FirstName.Trim(),
        LastName = request.LastName.Trim(),
        Email = request.Email.Trim(),
        DateOfBirth = request.DateOfBirth,
        Status = "ACT",
        PhoneNumber = request.PhoneNumber?.Trim()
    };
    database.Add(staff);

    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/staff/{staff.StaffId}", new { staff.StaffId, staff.UserId });
});

app.MapGet("/api/students", async (AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
    (await database.Students.AsNoTracking()
        .Include(student => student.Enrollments).ThenInclude(enrollment => enrollment.AcademicTerm)
        .OrderBy(value => value.LastName)
        .ThenBy(value => value.FirstName)
        .ToListAsync(cancellationToken)).Select(CreateStudentProfileResponse));

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

    var requestedTerm = request.AcademicTermId is null
        ? null
        : await database.AcademicTerms.SingleOrDefaultAsync(term => term.TermId == request.AcademicTermId, cancellationToken);
    if (request.AcademicTermId is not null && requestedTerm is null)
    {
        return Results.NotFound("Academic term was not found.");
    }

    var student = new Student
    {
        StudentId = await CreateStudentIdAsync(database, cancellationToken),
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

    var requestedTerm = await database.AcademicTerms
        .SingleOrDefaultAsync(term => term.TermId == request.AcademicTermId, cancellationToken);
    if (requestedTerm is null)
    {
        return Results.NotFound("Academic term was not found.");
    }

    var enrollment = new Enrollment
    {
        StudentId = student.UserId,
        CourseCode = request.CourseCode.Trim(),
        AcademicTermId = request.AcademicTermId,
        AcademicYear = request.AcademicYear ?? DateTime.UtcNow.Year
    };
    database.Enrollments.Add(enrollment);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/students/{studentId}/enrollments/{enrollment.EnrollmentId}", enrollment.EnrollmentId);
});

app.MapPut("/api/enrollments/{enrollmentId:int}/grade", async (int enrollmentId, GradeEntryRequest request,
    AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    if (request.GradeValue is < 0 or > 100)
    {
        return Results.BadRequest("Grade must be between 0 and 100.");
    }

    var enrollment = await database.Enrollments
        .Include(value => value.AcademicTerm)
        .SingleOrDefaultAsync(value => value.EnrollmentId == enrollmentId, cancellationToken);
    if (enrollment is null)
    {
        return Results.NotFound("Enrollment was not found.");
    }

    if (enrollment.AcademicTerm is null)
    {
        return Results.BadRequest("The enrollment does not have an academic session.");
    }

    var sessionDates = EnrollmentStatusRules.GetSessionDates(
        enrollment.AcademicYear,
        enrollment.AcademicTerm.StartWeekOfYear,
        enrollment.AcademicTerm.EndWeekOfYear);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (today < sessionDates.StartDate)
    {
        return Results.BadRequest("Grades cannot be assigned before the course starts.");
    }

    if (today > sessionDates.EndDate)
    {
        return Results.BadRequest("Grades cannot be edited after the course ends.");
    }

    enrollment.GradeValue = request.GradeValue;
    enrollment.GradeDate = today;
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { enrollment.EnrollmentId, enrollment.GradeValue, enrollment.Status, enrollment.GradeDate });
});

app.MapGet("/api/students/{studentId}", async (string studentId, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var value = await database.Set<Student>().AsNoTracking()
        .Include(student => student.Enrollments).ThenInclude(enrollment => enrollment.AcademicTerm)
        .SingleOrDefaultAsync(student => student.StudentId == studentId, cancellationToken);
    var student = value is null ? null : CreateStudentProfileResponse(value);
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

app.MapPut("/api/students/{studentId}/graduation", async (string studentId, GraduationUpdateRequest request,
    AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var student = await database.Students.SingleOrDefaultAsync(value => value.StudentId == studentId, cancellationToken);
    if (student is null)
    {
        return Results.NotFound("Student was not found.");
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (request.GraduationDate > today)
    {
        return Results.BadRequest("Graduation date cannot be in the future.");
    }

    student.GraduationDate = request.GraduationDate;
    await database.SaveChangesAsync(cancellationToken);
    return Results.Ok(new GraduationResponse(student.StudentId, request.GraduationDate));
});

app.MapPut("/api/staff/{staffId}", async (string staffId, UpdateStaffRequest request, AcademicRecordsDbContext database, CancellationToken cancellationToken) =>
{
    var staff = await database.Set<Staff>().SingleOrDefaultAsync(value => value.StaffId == staffId, cancellationToken);
    if (staff is null)
    {
        return Results.NotFound("Staff member was not found.");
    }

    if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
        string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null)
    {
        return Results.BadRequest("First name, last name, email, and date of birth are required.");
    }

    if (!IsValidEmail(request.Email))
    {
        return Results.BadRequest("Email address is not valid.");
    }

    if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !IsValidPhoneNumber(request.PhoneNumber))
    {
        return Results.BadRequest("Phone number is not valid. Use format: 0400000000 or +61400000000");
    }

    staff.FirstName = request.FirstName.Trim();
    staff.LastName = request.LastName.Trim();
    staff.Email = request.Email.Trim();
    staff.DateOfBirth = request.DateOfBirth;
    staff.PhoneNumber = request.PhoneNumber?.Trim();

    await database.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.Run();

static async Task<string> CreateStudentIdAsync(AcademicRecordsDbContext database, CancellationToken cancellationToken)
{
    var maxSequence = await database.Students
        .Select(student => student.StudentId)
        .ToListAsync(cancellationToken);

    var nextSequence = maxSequence
        .Select(studentId => int.TryParse(studentId.AsSpan(3), out var sequence) ? sequence : 0)
        .DefaultIfEmpty(0)
        .Max() + 1;

    return $"STU{nextSequence:D3}";
}

static async Task<string> CreateStaffIdAsync(AcademicRecordsDbContext database, CancellationToken cancellationToken)
{
    var existingStaffIds = await database.Set<Staff>()
        .Select(staff => staff.StaffId)
        .ToListAsync(cancellationToken);

    var nextSequence = existingStaffIds
        .Select(staffId => int.TryParse(staffId.AsSpan(3), out var sequence) ? sequence : 0)
        .DefaultIfEmpty(0)
        .Max() + 1;

    return $"STF{nextSequence:D3}";
}

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

static StudentProfileResponse CreateStudentProfileResponse(Student value) =>
    new(value.UserId, value.StudentId, value.FirstName, value.LastName, value.Email,
        value.DateOfBirth, value.PhoneNumber, value.MailingAddress, value.EmergencyContactName, value.EmergencyContactPhone,
        value.EnrollmentDate, value.Degree, value.Major, value.SubMajor,
        value.Enrollments.Select(enrollment => new TermEnrollmentResponse(enrollment.EnrollmentId, enrollment.CourseCode,
            enrollment.AcademicTermId, enrollment.AcademicTerm?.TermCode, enrollment.AcademicTerm?.TermName,
            enrollment.AcademicTerm?.StartWeekOfYear ?? 0, enrollment.AcademicTerm?.EndWeekOfYear ?? 0,
            enrollment.AcademicYear, enrollment.Status, null,
            enrollment.GradeValue, enrollment.IsFinalized)).ToList(),
        value.GraduationDate);

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
    string? TermName, int StartWeekOfYear, int EndWeekOfYear, int AcademicYear, string Status, string? GradeType, decimal? GradeValue, bool IsFinalized);

public sealed record UpdateStudentRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string MailingAddress, string EmergencyContactName, string EmergencyContactPhone, string? PhoneNumber = null,
    string? Degree = null, string? Major = null, string? SubMajor = null);

public sealed record UpdateStaffRequest(string FirstName, string LastName, string Email, DateOnly? DateOfBirth,
    string? PhoneNumber = null);

public sealed record GraduationUpdateRequest(DateOnly GraduationDate);

public sealed record GraduationResponse(string StudentId, DateOnly GraduationDate);

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

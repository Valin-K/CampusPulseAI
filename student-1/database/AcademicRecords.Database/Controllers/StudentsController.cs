using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using AcademicRecords.Database.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Database.Controllers;

[ApiController]
[Route("api/students")]
public sealed class StudentsController(AcademicRecordsDbContext database) : ControllerBase
{
    [HttpGet("by-user/{userId:int}")]
    public async Task<ActionResult<StudentProfileResponse>> GetByUser(
        int userId,
        CancellationToken cancellationToken)
    {
        var value = await database.Students.AsNoTracking()
            .Include(student => student.Enrollments).ThenInclude(enrollment => enrollment.AcademicTerm)
            .SingleOrDefaultAsync(student => student.UserId == userId, cancellationToken);
        var student = value is null ? null : CreateStudentProfileResponse(value);

        return student is null ? NotFound() : Ok(student);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StudentProfileResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var students = await database.Students.AsNoTracking()
            .Include(student => student.Enrollments).ThenInclude(enrollment => enrollment.AcademicTerm)
            .OrderBy(value => value.LastName)
            .ThenBy(value => value.FirstName)
            .ToListAsync(cancellationToken);

        return Ok(students.Select(CreateStudentProfileResponse));
    }

    [HttpPost("enroll")]
    [AllowAnonymous]
    public async Task<IActionResult> Enroll(
        EnrollStudentRequest request,
        CancellationToken cancellationToken)
    {
        if (!UserPasswordSecurity.IsValidNewPassword(request.Password, request.ConfirmPassword))
        {
            return BadRequest("Password must be 12 to 128 characters and match the confirmation.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null ||
            string.IsNullOrWhiteSpace(request.MailingAddress) || string.IsNullOrWhiteSpace(request.EmergencyContactName) ||
            string.IsNullOrWhiteSpace(request.EmergencyContactPhone))
        {
            return BadRequest("First name, last name, email, date of birth, mailing address, and emergency contact details are required.");
        }

        if (request.AcademicTermId is not null && string.IsNullOrWhiteSpace(request.CourseCode))
        {
            return BadRequest("Course code is required when an academic term is supplied.");
        }

        var requestedTerm = request.AcademicTermId is null
            ? null
            : await database.AcademicTerms.SingleOrDefaultAsync(term => term.TermId == request.AcademicTermId, cancellationToken);
        if (request.AcademicTermId is not null && requestedTerm is null)
        {
            return NotFound("Academic term was not found.");
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
        student.PasswordHash = UserPasswordSecurity.HashPassword(student, request.Password);
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
        return Created($"/api/students/{student.StudentId}", new { student.StudentId, student.UserId });
    }

    [HttpPost("{studentId}/enrollments")]
    public async Task<IActionResult> AddEnrollment(
        string studentId,
        AddEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var student = await database.Set<Student>()
            .SingleOrDefaultAsync(value => value.StudentId == studentId, cancellationToken);
        if (student is null)
        {
            return NotFound("Student was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.CourseCode) || request.AcademicTermId <= 0)
        {
            return BadRequest("Course code and academic term are required.");
        }

        var requestedTerm = await database.AcademicTerms
            .SingleOrDefaultAsync(term => term.TermId == request.AcademicTermId, cancellationToken);
        if (requestedTerm is null)
        {
            return NotFound("Academic term was not found.");
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
        return Created($"/api/students/{studentId}/enrollments/{enrollment.EnrollmentId}", enrollment.EnrollmentId);
    }

    [HttpGet("{studentId}")]
    public async Task<ActionResult<StudentProfileResponse>> GetByStudentId(
        string studentId,
        CancellationToken cancellationToken)
    {
        var value = await database.Set<Student>().AsNoTracking()
            .Include(student => student.Enrollments).ThenInclude(enrollment => enrollment.AcademicTerm)
            .SingleOrDefaultAsync(student => student.StudentId == studentId, cancellationToken);
        var student = value is null ? null : CreateStudentProfileResponse(value);

        return student is null ? NotFound() : Ok(student);
    }

    [HttpPut("{studentId}")]
    public async Task<IActionResult> Update(
        string studentId,
        UpdateStudentRequest request,
        CancellationToken cancellationToken)
    {
        var student = await database.Students
            .SingleOrDefaultAsync(value => value.StudentId == studentId, cancellationToken);
        if (student is null)
        {
            return NotFound("Student was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null ||
            string.IsNullOrWhiteSpace(request.MailingAddress) || string.IsNullOrWhiteSpace(request.EmergencyContactName) ||
            string.IsNullOrWhiteSpace(request.EmergencyContactPhone))
        {
            return BadRequest("First name, last name, email, date of birth, mailing address, and emergency contact details are required.");
        }

        if (!IsValidEmail(request.Email))
        {
            return BadRequest("Email address is not valid.");
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !IsValidPhoneNumber(request.PhoneNumber))
        {
            return BadRequest("Phone number is not valid. Use format: 0400000000 or +61400000000");
        }

        if (!IsValidPhoneNumber(request.EmergencyContactPhone))
        {
            return BadRequest("Emergency contact phone number is not valid. Use format: 0400000000 or +61400000000");
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
        return NoContent();
    }

    [HttpPut("{studentId}/graduation")]
    public async Task<IActionResult> UpdateGraduation(
        string studentId,
        GraduationUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var student = await database.Students
            .SingleOrDefaultAsync(value => value.StudentId == studentId, cancellationToken);
        if (student is null)
        {
            return NotFound("Student was not found.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.GraduationDate > today)
        {
            return BadRequest("Graduation date cannot be in the future.");
        }

        student.GraduationDate = request.GraduationDate;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(new GraduationResponse(student.StudentId, request.GraduationDate));
    }

    private static async Task<string> CreateStudentIdAsync(
        AcademicRecordsDbContext database,
        CancellationToken cancellationToken)
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

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new System.Net.Mail.MailAddress(email);
            return address.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidPhoneNumber(string phoneNumber)
    {
        var cleaned = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (phoneNumber.StartsWith("+61"))
        {
            return cleaned.Length == 11 && cleaned.StartsWith("61");
        }

        return cleaned.Length == 10 && cleaned.StartsWith("0");
    }

    private static StudentProfileResponse CreateStudentProfileResponse(Student value) =>
        new(value.UserId, value.StudentId, value.FirstName, value.LastName, value.Email,
            value.DateOfBirth, value.PhoneNumber, value.MailingAddress, value.EmergencyContactName, value.EmergencyContactPhone,
            value.EnrollmentDate, value.Degree, value.Major, value.SubMajor,
            value.Enrollments.Select(enrollment => new TermEnrollmentResponse(enrollment.EnrollmentId, enrollment.CourseCode,
                enrollment.AcademicTermId, enrollment.AcademicTerm?.TermCode, enrollment.AcademicTerm?.TermName,
                enrollment.AcademicTerm?.StartWeekOfYear ?? 0, enrollment.AcademicTerm?.EndWeekOfYear ?? 0,
                enrollment.AcademicYear, enrollment.Status, null,
                enrollment.GradeValue, enrollment.IsFinalized)).ToList(),
            value.GraduationDate);
}

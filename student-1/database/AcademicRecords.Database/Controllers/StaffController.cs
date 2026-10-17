using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using AcademicRecords.Database.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Database.Controllers;

[ApiController]
[Route("api/staff")]
public sealed class StaffController(AcademicRecordsDbContext database) : ControllerBase
{
    [HttpGet("by-user/{userId:int}")]
    public async Task<ActionResult<StaffProfileResponse>> GetByUser(
        int userId,
        CancellationToken cancellationToken)
    {
        var staff = await database.Set<Staff>().AsNoTracking()
            .Where(value => value.UserId == userId)
            .Select(value => new StaffProfileResponse(value.UserId, value.StaffId, value.FirstName, value.LastName,
                value.Email, value.DateOfBirth, value.PhoneNumber))
            .SingleOrDefaultAsync(cancellationToken);

        return staff is null ? NotFound() : Ok(staff);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StaffProfileResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var staff = await database.Set<Staff>().AsNoTracking()
            .OrderBy(value => value.LastName)
            .ThenBy(value => value.FirstName)
            .Select(value => new StaffProfileResponse(value.UserId, value.StaffId, value.FirstName, value.LastName,
                value.Email, value.DateOfBirth, value.PhoneNumber))
            .ToListAsync(cancellationToken);

        return Ok(staff);
    }

    [HttpPost("enroll")]
    [AllowAnonymous]
    public async Task<IActionResult> Enroll(
        EnrollStaffRequest request,
        CancellationToken cancellationToken)
    {
        if (!UserPasswordSecurity.IsValidNewPassword(request.Password, request.ConfirmPassword))
        {
            return BadRequest("Password must be 12 to 128 characters and match the confirmation.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null)
        {
            return BadRequest("First name, last name, email, and date of birth are required.");
        }

        if (!IsValidEmail(request.Email))
        {
            return BadRequest("Email address is not valid.");
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !IsValidPhoneNumber(request.PhoneNumber))
        {
            return BadRequest("Phone number is not valid. Use format: 0400000000 or +61400000000");
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
        staff.PasswordHash = UserPasswordSecurity.HashPassword(staff, request.Password);
        database.Add(staff);

        await database.SaveChangesAsync(cancellationToken);
        return Created($"/api/staff/{staff.StaffId}", new { staff.StaffId, staff.UserId });
    }

    [HttpPut("{staffId}")]
    public async Task<IActionResult> Update(
        string staffId,
        UpdateStaffRequest request,
        CancellationToken cancellationToken)
    {
        var staff = await database.Set<Staff>()
            .SingleOrDefaultAsync(value => value.StaffId == staffId, cancellationToken);
        if (staff is null)
        {
            return NotFound("Staff member was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email) || request.DateOfBirth is null)
        {
            return BadRequest("First name, last name, email, and date of birth are required.");
        }

        if (!IsValidEmail(request.Email))
        {
            return BadRequest("Email address is not valid.");
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !IsValidPhoneNumber(request.PhoneNumber))
        {
            return BadRequest("Phone number is not valid. Use format: 0400000000 or +61400000000");
        }

        staff.FirstName = request.FirstName.Trim();
        staff.LastName = request.LastName.Trim();
        staff.Email = request.Email.Trim();
        staff.DateOfBirth = request.DateOfBirth;
        staff.PhoneNumber = request.PhoneNumber?.Trim();

        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static async Task<string> CreateStaffIdAsync(
        AcademicRecordsDbContext database,
        CancellationToken cancellationToken)
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
}

using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using AcademicRecords.Database.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Database.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(AcademicRecordsDbContext database) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await database.Users
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
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [AllowAnonymous]
    [HttpPost("authenticate")]
    public async Task<ActionResult<UserResponse>> Authenticate(
        AuthenticateUserRequest request,
        CancellationToken cancellationToken)
    {
        var role = request.Role.Trim();
        User? user = null;
        if (role.Equals("Student", StringComparison.OrdinalIgnoreCase))
        {
            user = await database.Students.AsNoTracking()
                .SingleOrDefaultAsync(value => value.StudentId.ToLower() == request.AccountId.ToLower(), cancellationToken);
        }
        else if (role.Equals("Staff", StringComparison.OrdinalIgnoreCase))
        {
            user = await database.Users.OfType<Staff>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.StaffId.ToLower() == request.AccountId.ToLower(), cancellationToken);
        }
        var roleMatches = user is Student && role.Equals("Student", StringComparison.OrdinalIgnoreCase)
            || user is Staff && role.Equals("Staff", StringComparison.OrdinalIgnoreCase);
        if (user is null || !roleMatches || !UserPasswordSecurity.VerifyPassword(user, request.Password))
        {
            return Unauthorized();
        }

        return Ok(new UserResponse(
            user.UserId,
            user.FirstName,
            user.LastName,
            user.Email,
            user is Student ? "Student" : "Staff",
            user is Student student ? student.StudentId : null,
            user is Staff staff ? staff.StaffId : null));
    }
}

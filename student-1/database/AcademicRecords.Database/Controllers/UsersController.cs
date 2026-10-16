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
        var user = await database.Users.AsNoTracking()
            .SingleOrDefaultAsync(value => value.UserId == request.UserId, cancellationToken);
        var roleMatches = user is Student && string.Equals(request.Role, "Student", StringComparison.OrdinalIgnoreCase)
            || user is Staff && string.Equals(request.Role, "Staff", StringComparison.OrdinalIgnoreCase);
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

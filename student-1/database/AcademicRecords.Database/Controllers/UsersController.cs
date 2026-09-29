using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
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
}

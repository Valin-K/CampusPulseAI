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

app.Run();

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

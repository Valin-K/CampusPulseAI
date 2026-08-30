using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
var connectionString = builder.Configuration.GetConnectionString("AcademicRecords")
    ?? "Data Source=../../database/AcademicRecords.Database/Data/academic-records.db";
var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
if (!string.IsNullOrWhiteSpace(sqliteConnection.DataSource) && sqliteConnection.DataSource != ":memory:")
{
    var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(sqliteConnection.DataSource));
    if (databaseDirectory is not null)
    {
        Directory.CreateDirectory(databaseDirectory);
    }
}
builder.Services.AddDbContext<AcademicRecordsDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins("http://localhost:5062", "http://localhost:8081")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AcademicRecordsDbContext>().Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.UseCors("Frontend");

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

app.MapControllers();

app.Run();

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

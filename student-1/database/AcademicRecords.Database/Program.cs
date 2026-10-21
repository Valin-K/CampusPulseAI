using AcademicRecords.Database.Data;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("AcademicRecords")
    ?? "Data Source=/app/data/academic-records.db";

builder.Services.AddDbContext<AcademicRecordsDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddControllers();
builder.Services.AddCampusPulseSecurity(builder.Configuration);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "academic-records-database", status = "healthy" })).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AcademicRecordsDbContext>();
    await database.Database.EnsureCreatedAsync();
    await AcademicRecordsSeedData.InitializeAsync(database);
}

app.MapControllers();
app.Run();


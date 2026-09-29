using AcademicRecords.Database.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("AcademicRecords")
    ?? "Data Source=/app/data/academic-records.db";

builder.Services.AddDbContext<AcademicRecordsDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "academic-records-database", status = "healthy" }));

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AcademicRecordsDbContext>();
    await database.Database.EnsureCreatedAsync();
    await AcademicRecordsSeedData.InitializeAsync(database);
}

app.MapControllers();
app.Run();


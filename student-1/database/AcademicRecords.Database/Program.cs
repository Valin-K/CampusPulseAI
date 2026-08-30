using AcademicRecords.Database.Data;
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

app.Run();
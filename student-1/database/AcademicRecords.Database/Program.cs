using AcademicRecords.Database.Data;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("AcademicRecords")
    ?? "Data Source=/app/data/academic-records.db";

var sharedConnection = new SqliteConnection(connectionString);
sharedConnection.Open();
using (var pragma = sharedConnection.CreateCommand())
{
    pragma.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA locking_mode=EXCLUSIVE;";
    pragma.ExecuteNonQuery();
}
builder.Services.AddSingleton(sharedConnection);
builder.Services.AddDbContext<AcademicRecordsDbContext>(options => options.UseSqlite(sharedConnection));
builder.Services.AddControllers();
builder.Services.AddCampusPulseSecurity(builder.Configuration);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "academic-records-database", status = "healthy" })).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AcademicRecordsDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await database.Database.EnsureCreatedAsync();
            await AcademicRecordsSeedData.InitializeAsync(database);
            break;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < 10)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }
}

app.MapControllers();
app.Run();


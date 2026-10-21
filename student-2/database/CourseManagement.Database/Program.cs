using CourseManagement.Backend.Services;
using CourseManagement.Database.Data;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(
        "CourseManagementDatabase")
    ?? "Data Source=/app/data/course-management.db;Foreign Keys=True";
// Azure Files (SMB) does not reliably support the byte-range locking SQLite needs to acquire
// and release a write lock per transaction - every repeated lock negotiation is a chance to see
// "database is locked". Instead, acquire the lock ONCE via a single connection kept open and
// exclusive for this container's entire lifetime, and have every DbContext reuse it.
var sharedConnection = new SqliteConnection(connectionString);
sharedConnection.Open();
using (var pragma = sharedConnection.CreateCommand())
{
    pragma.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA locking_mode=EXCLUSIVE;";
    pragma.ExecuteNonQuery();
}
builder.Services.AddSingleton(sharedConnection);
builder.Services.AddDbContext<CourseManagementDbContext>(options =>
    options.UseSqlite(sharedConnection));
builder.Services.AddControllers();
builder.Services.AddCampusPulseSecurity(builder.Configuration);
builder.Services
    .AddOptions<SyllabusStorageOptions>()
    .Bind(builder.Configuration.GetSection(SyllabusStorageOptions.SectionName))
    .PostConfigure(options =>
    {
        if (!Path.IsPathRooted(options.RootPath))
        {
            options.RootPath = Path.GetFullPath(
                options.RootPath,
                builder.Environment.ContentRootPath);
        }
    })
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.RootPath),
        "The syllabus storage root path is required.")
    .Validate(
        options => options.MaxFileSizeBytes > 0,
        "The syllabus maximum file size must be greater than zero.")
    .ValidateOnStart();
var maxSyllabusFileSize = builder.Configuration.GetValue<long?>(
        $"{SyllabusStorageOptions.SectionName}:MaxFileSizeBytes")
    ?? SyllabusStorageOptions.DefaultMaxFileSizeBytes;
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize =
        maxSyllabusFileSize + (1024 * 1024));
builder.Services.AddSingleton<ISyllabusStorage, FileSystemSyllabusStorage>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider
        .GetRequiredService<CourseManagementDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await CourseManagementSchema.EnsureCurrentAsync(database);
            await CourseManagementSeedData.InitializeAsync(database);
            break;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < 10)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

await app.RunAsync();

public partial class Program;

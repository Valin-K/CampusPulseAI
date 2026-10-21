using CourseManagement.Backend.Services;
using CourseManagement.Database.Data;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(
        "CourseManagementDatabase")
    ?? "Data Source=/app/data/course-management.db;Foreign Keys=True";
builder.Services.AddDbContext<CourseManagementDbContext>(options =>
    options.UseSqlite(connectionString));
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
    await CourseManagementSchema.EnsureCurrentAsync(database);
    await CourseManagementSeedData.InitializeAsync(database);
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

await app.RunAsync();

public partial class Program;

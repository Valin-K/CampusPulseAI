using CourseManagement.Database.Data;
using CourseManagement.Backend.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
const string courseManagementFrontendCorsPolicy = "CourseManagementFrontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? throw new InvalidOperationException("Course Management CORS origins are not configured.");
builder.Services.AddCors(options =>
{
    options.AddPolicy(courseManagementFrontendCorsPolicy, policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
var connectionString = builder.Configuration.GetConnectionString("CourseManagementDatabase")
    ?? throw new InvalidOperationException("Course Management database connection string is not configured.");
builder.Services.AddDbContext<CourseManagementDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services
    .AddOptions<SyllabusStorageOptions>()
    .Bind(builder.Configuration.GetSection(SyllabusStorageOptions.SectionName))
    .PostConfigure(options =>
    {
        if (!Path.IsPathRooted(options.RootPath))
        {
            options.RootPath = Path.GetFullPath(options.RootPath, builder.Environment.ContentRootPath);
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
    options.Limits.MaxRequestBodySize = maxSyllabusFileSize + (1024 * 1024));
builder.Services.AddSingleton<ISyllabusStorage, FileSystemSyllabusStorage>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<CourseManagementDbContext>();
    await CourseManagementDataInitializer.InitializeAsync(database);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(courseManagementFrontendCorsPolicy);

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

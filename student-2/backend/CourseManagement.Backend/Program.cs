using CourseManagement.Database.Data;
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

using CourseManagement.Backend.Clients;
using CourseManagement.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services
    .AddOptions<CourseAccessOptions>()
    .Bind(builder.Configuration.GetSection(CourseAccessOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DemoStudentId),
        "The Course access demonstration Student ID is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DemoStaffId),
        "The Course access demonstration Staff ID is required.")
    .Validate(
        options => options.DemoEnrollmentTermId > 0,
        "The Course access demonstration enrolment term is required.")
    .Validate(
        options => options.DemoAcademicYear > 0,
        "The Course access demonstration academic year is required.")
    .ValidateOnStart();
var academicRecordsDatabaseAddress =
    builder.Configuration["Services:AcademicRecordsDatabase"]
    ?? throw new InvalidOperationException(
        "The Academic Records database service address is not configured.");
builder.Services.AddHttpClient<IAcademicRecordsClient, AcademicRecordsClient>(client =>
{
    client.BaseAddress = new Uri(
        academicRecordsDatabaseAddress,
        UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(5);
});
var courseManagementDatabaseAddress =
    builder.Configuration["Services:CourseManagementDatabase"]
    ?? throw new InvalidOperationException(
        "The Course Management database service address is not configured.");
builder.Services.AddHttpClient(
    DatabaseApiProxy.ClientName,
    client =>
    {
        client.BaseAddress = new Uri(
            courseManagementDatabaseAddress,
            UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddHttpClient<ICourseCatalogueClient, CourseCatalogueClient>(
    client =>
    {
        client.BaseAddress = new Uri(
            courseManagementDatabaseAddress,
            UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(5);
    });
builder.Services.AddScoped<DatabaseApiProxy>();
builder.Services.AddScoped<ICourseQuestionService, GeminiCourseQuestionService>();
var geminiApiKey = builder.Configuration["GEMINI_API_KEY"];
var geminiApiAddress = builder.Configuration["Gemini:BaseUrl"]
    ?? "https://generativelanguage.googleapis.com/";
builder.Services.AddHttpClient(
    GeminiCourseQuestionService.ClientName,
    client =>
    {
        client.BaseAddress = new Uri(geminiApiAddress, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(90);

        if (!string.IsNullOrWhiteSpace(geminiApiKey))
        {
            client.DefaultRequestHeaders.Add(
                "x-goog-api-key",
                geminiApiKey);
        }
    });
builder.Services.AddOpenApi();

var app = builder.Build();

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

MapDatabaseProxy(app, "/api/courses");
MapDatabaseProxy(app, "/api/prerequisites");
MapDatabaseProxy(app, "/api/faculty-assignments");
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

await app.RunAsync();

static void MapDatabaseProxy(WebApplication app, string routePrefix)
{
    string[] methods = ["GET", "POST", "PUT", "DELETE", "PATCH"];

    app.MapMethods(
        routePrefix,
        methods,
        async (
            HttpContext context,
            DatabaseApiProxy proxy,
            CancellationToken cancellationToken) =>
            await proxy.ForwardAsync(
                context,
                routePrefix,
                cancellationToken));

    app.MapMethods(
        $"{routePrefix}/{{**path}}",
        methods,
        async (
            HttpContext context,
            string path,
            DatabaseApiProxy proxy,
            CancellationToken cancellationToken) =>
            await proxy.ForwardAsync(
                context,
                $"{routePrefix}/{path}",
                cancellationToken));
}

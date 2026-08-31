using System.Net.Http.Json;
using AcademicRecords.Backend;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient("academicRecordsDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://academicrecords.database:8080");
});
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins("http://localhost:5062", "http://localhost:8081")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.UseCors("Frontend");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/users", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync("/api/users", cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/students/by-user/{userId:int}", async (int userId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync($"/api/students/by-user/{userId}", cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/staff/by-user/{userId:int}", async (int userId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync($"/api/staff/by-user/{userId}", cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/staff", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync("/api/staff", cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/students", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync("/api/students", cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/students/enroll", async (HttpRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var databaseRequest = new HttpRequestMessage(HttpMethod.Post, "/api/students/enroll")
    {
        Content = new StreamContent(request.Body)
    };
    databaseRequest.Content.Headers.ContentType = new("application/json");
    using var response = await clients.CreateClient("academicRecordsDatabase").SendAsync(databaseRequest, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/staff/enroll", async (HttpRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var databaseRequest = new HttpRequestMessage(HttpMethod.Post, "/api/staff/enroll")
    {
        Content = new StreamContent(request.Body)
    };
    databaseRequest.Content.Headers.ContentType = new("application/json");
    using var response = await clients.CreateClient("academicRecordsDatabase").SendAsync(databaseRequest, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/students/{studentId}/enrollments", async (string studentId, HttpRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var databaseRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/students/{Uri.EscapeDataString(studentId)}/enrollments")
    {
        Content = new StreamContent(request.Body)
    };
    databaseRequest.Content.Headers.ContentType = new("application/json");
    using var response = await clients.CreateClient("academicRecordsDatabase").SendAsync(databaseRequest, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/students/{studentId}", async (string studentId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync($"/api/students/{Uri.EscapeDataString(studentId)}", cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/students/{studentId}/wam", async (string studentId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await GetStudentStatisticAsync(studentId, false, clients, cancellationToken));

app.MapGet("/api/students/{studentId}/gpa", async (string studentId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await GetStudentStatisticAsync(studentId, true, clients, cancellationToken));

app.MapPut("/api/students/{studentId}", async (string studentId, HttpRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var databaseRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/students/{Uri.EscapeDataString(studentId)}")
    {
        Content = new StreamContent(request.Body)
    };
    databaseRequest.Content.Headers.ContentType = new("application/json");
    using var response = await clients.CreateClient("academicRecordsDatabase").SendAsync(databaseRequest, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapControllers();

app.Run();

static async Task<IResult> GetStudentStatisticAsync(string studentId, bool gpa, IHttpClientFactory clients, CancellationToken cancellationToken)
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync($"/api/students/{Uri.EscapeDataString(studentId)}", cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
    }

    var profile = await response.Content.ReadFromJsonAsync<StudentStatisticsProfile>(cancellationToken);
    if (profile is null)
    {
        return Results.Problem("The student profile response was invalid.");
    }

    var enrollments = (profile.Enrollments ?? []).Select(enrollment =>
        new GradedEnrollment(enrollment.GradeValue, enrollment.Credits, enrollment.IsFinalized));
    var statistic = gpa
        ? StudentStatisticsCalculator.CalculateGpa(enrollments)
        : StudentStatisticsCalculator.CalculateWam(enrollments);

    return Results.Ok(new StudentStatisticResponse(profile.StudentId, gpa ? "GPA" : "WAM",
        statistic.Value, statistic.GradedEnrollmentCount, statistic.TotalCredits));
}

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

public sealed record StudentStatisticsProfile(string StudentId, List<StudentStatisticsEnrollment>? Enrollments);

public sealed record StudentStatisticsEnrollment(decimal? Credits, decimal? GradeValue, bool IsFinalized);

public sealed record StudentStatisticResponse(
    string StudentId,
    string Statistic,
    decimal? Value,
    int GradedEnrollmentCount,
    decimal TotalCredits);

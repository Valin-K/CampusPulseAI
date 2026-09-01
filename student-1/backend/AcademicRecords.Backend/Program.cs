using System.Net.Http.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient("academicRecordsDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://academicrecords.database:8080");
});
builder.Services.AddHttpClient("courseManagementDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CourseManagementDatabase"] ?? "http://coursemanagement.database:8080");
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

app.MapGet("/api/students/by-user/{userId:int}", (int userId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    ForwardStudentResponseAsync($"/api/students/by-user/{userId}", clients, cancellationToken));

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

app.MapGet("/api/students", (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    ForwardStudentResponseAsync("/api/students", clients, cancellationToken));

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

app.MapGet("/api/students/{studentId}", (string studentId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    ForwardStudentResponseAsync($"/api/students/{Uri.EscapeDataString(studentId)}", clients, cancellationToken));

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

static async Task<IResult> ForwardStudentResponseAsync(
    string path,
    IHttpClientFactory clients,
    CancellationToken cancellationToken)
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync(path, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";

    if (!response.IsSuccessStatusCode)
    {
        return Results.Content(content, mediaType, statusCode: (int)response.StatusCode);
    }

    var courses = await GetCourseCreditsAsync(clients, cancellationToken);
    var document = JsonNode.Parse(content);
    if (document is JsonArray students)
    {
        foreach (var student in students.OfType<JsonObject>())
        {
            AddCourseCredits(student, courses);
        }
    }
    else if (document is JsonObject student)
    {
        AddCourseCredits(student, courses);
    }

    return Results.Content(document?.ToJsonString() ?? content, mediaType, statusCode: (int)response.StatusCode);
}

static async Task<IReadOnlyDictionary<string, int>> GetCourseCreditsAsync(
    IHttpClientFactory clients,
    CancellationToken cancellationToken)
{
    try
    {
        var courses = await clients.CreateClient("courseManagementDatabase")
            .GetFromJsonAsync<CourseCreditResponse[]>("/api/courses", cancellationToken);

        return courses?.ToDictionary(course => course.Code, course => course.CreditPoints, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
    catch (HttpRequestException)
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}

static void AddCourseCredits(JsonObject student, IReadOnlyDictionary<string, int> courses)
{
    if (student["enrollments"] is not JsonArray enrollments)
    {
        return;
    }

    foreach (var enrollment in enrollments.OfType<JsonObject>())
    {
        var courseCode = enrollment["courseCode"]?.GetValue<string>();
        enrollment["credits"] = courseCode is not null && courses.TryGetValue(courseCode, out var creditPoints)
            ? creditPoints
            : null;
    }
}

public sealed record CourseCreditResponse(string Code, int CreditPoints);

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

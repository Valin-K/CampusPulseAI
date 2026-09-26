using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AcademicRecords.Backend;
using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Generation;
using QuestPDF.Infrastructure;

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
var geminiApiKey = builder.Configuration["GEMINI_API_KEY"] ?? builder.Configuration["Gemini:ApiKey"];
builder.Services.AddHttpClient("gemini", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(90);
    if (!string.IsNullOrWhiteSpace(geminiApiKey))
    {
        client.DefaultRequestHeaders.Add("x-goog-api-key", geminiApiKey);
    }
});
builder.Services.AddSingleton<ITranscriptPdfGenerator, TranscriptPdfGenerator>();
builder.Services.AddScoped<GeminiAcademicProgressService>();
builder.Services.AddHttpClient<IRagServerClient, RagServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton<RagCitationParser>();
builder.Services.AddScoped<RagKnowledgeService>();
builder.Services.AddScoped<IAcademicKnowledgeService, AcademicKnowledgeService>();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins("http://localhost:5062", "http://localhost:8081")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

QuestPDF.Settings.License = LicenseType.Community;

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

app.MapPut("/api/enrollments/{enrollmentId:int}/grade", async (int enrollmentId, HttpRequest request,
    IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var databaseRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/enrollments/{enrollmentId}/grade")
    {
        Content = new StreamContent(request.Body)
    };
    databaseRequest.Content.Headers.ContentType = new("application/json");
    using var response = await clients.CreateClient("academicRecordsDatabase").SendAsync(databaseRequest, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/courses", (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    ForwardCourseManagementResponseAsync("/api/courses", clients, cancellationToken));

app.MapGet("/api/faculty-assignments", (IHttpClientFactory clients, CancellationToken cancellationToken) =>
    ForwardCourseManagementResponseAsync("/api/faculty-assignments", clients, cancellationToken));

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

app.MapPut("/api/staff/{staffId}", async (string staffId, HttpRequest request, IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var databaseRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/staff/{Uri.EscapeDataString(staffId)}")
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

app.MapGet("/api/students/{studentId}/wam", async (string studentId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await GetStudentStatisticAsync(studentId, false, clients, cancellationToken));

app.MapGet("/api/students/{studentId}/gpa", async (string studentId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
    await GetStudentStatisticAsync(studentId, true, clients, cancellationToken));

app.MapGet("/api/students/{studentId}/statistics", async (
    string studentId,
    IHttpClientFactory clients,
    CancellationToken cancellationToken) =>
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

    var courses = await GetCourseMetadataAsync(clients, cancellationToken);
    profile = ApplyCourseMetadata(profile, courses);
    return Results.Ok(StudentAcademicStatisticsCalculator.Calculate(profile));
});

app.MapGet("/api/students/{studentId}/eligibility", async (string studentId, IHttpClientFactory clients, CancellationToken cancellationToken) =>
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

    var courses = await GetCourseMetadataAsync(clients, cancellationToken);
    profile = ApplyCourseMetadata(profile, courses);
    return Results.Ok(GraduationEligibilityCalculator.Calculate(profile));
});

app.MapPost("/api/students/{studentId}/ai-analysis", async (string studentId,
    IHttpClientFactory clients, GeminiAcademicProgressService service, CancellationToken cancellationToken) =>
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

    var courses = await GetCourseMetadataAsync(clients, cancellationToken);
    var analysis = await service.AnalyzeAsync(ApplyCourseMetadata(profile, courses), cancellationToken);
    return Results.Ok(analysis);
});

app.MapGet("/api/students/{studentId}/transcript.pdf", async (string studentId, IHttpClientFactory clients,
    ITranscriptPdfGenerator pdfGenerator, CancellationToken cancellationToken) =>
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

    var courses = await GetCourseMetadataAsync(clients, cancellationToken);
    profile = ApplyCourseMetadata(profile, courses);
    var pdf = pdfGenerator.Generate(TranscriptDataFactory.Create(profile, DateTime.Now));
    return Results.File(pdf, "application/pdf", $"transcript-{profile.StudentId}.pdf");
});

static async Task<IResult> ForwardCourseManagementResponseAsync(
    string path,
    IHttpClientFactory clients,
    CancellationToken cancellationToken)
{
    using var response = await clients.CreateClient("courseManagementDatabase")
        .GetAsync(path, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
}

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

app.MapPut("/api/students/{studentId}/graduation", async (string studentId, HttpRequest request,
    IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var databaseRequest = new HttpRequestMessage(HttpMethod.Put,
        $"/api/students/{Uri.EscapeDataString(studentId)}/graduation")
    {
        Content = new StreamContent(request.Body)
    };
    databaseRequest.Content.Headers.ContentType = new("application/json");
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .SendAsync(databaseRequest, cancellationToken);
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return Results.Content(content, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/ai/knowledge", async (KnowledgeQuestionRequest request,
    IAcademicKnowledgeService knowledge, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "A question is required." });
    }

    try
    {
        return Results.Ok(await knowledge.AskAsync(request, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            "The shared RAG server is unavailable. Start it on the host at http://localhost:5267 before using this feature.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem("The knowledge assistant did not respond in time.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
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

    var courses = await GetCourseMetadataAsync(clients, cancellationToken);
    profile = ApplyCourseMetadata(profile, courses);

    var enrollments = StudentStatisticsCalculator.ToGradedEnrollments(profile.Enrollments);
    var statistic = gpa
        ? StudentStatisticsCalculator.CalculateGpa(enrollments)
        : StudentStatisticsCalculator.CalculateWam(enrollments);

    return Results.Ok(new StudentStatisticResponse(profile.StudentId, gpa ? "GPA" : "WAM",
        statistic.Value, statistic.GradedEnrollmentCount, statistic.TotalCredits));
}

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

    var courses = await GetCourseMetadataAsync(clients, cancellationToken);
    var document = JsonNode.Parse(content);
    if (document is JsonArray students)
    {
        foreach (var student in students.OfType<JsonObject>())
        {
            AddCourseMetadata(student, courses);
        }
    }
    else if (document is JsonObject student)
    {
        AddCourseMetadata(student, courses);
    }

    return Results.Content(document?.ToJsonString() ?? content, mediaType, statusCode: (int)response.StatusCode);
}

static async Task<IReadOnlyDictionary<string, CourseMetadataResponse>> GetCourseMetadataAsync(
    IHttpClientFactory clients,
    CancellationToken cancellationToken)
{
    try
    {
        var courses = await clients.CreateClient("courseManagementDatabase")
            .GetFromJsonAsync<CourseMetadataResponse[]>("/api/courses", cancellationToken);

        return courses?.ToDictionary(course => course.Code, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CourseMetadataResponse>(StringComparer.OrdinalIgnoreCase);
    }
    catch (HttpRequestException)
    {
        return new Dictionary<string, CourseMetadataResponse>(StringComparer.OrdinalIgnoreCase);
    }
}

static void AddCourseMetadata(JsonObject student, IReadOnlyDictionary<string, CourseMetadataResponse> courses)
{
    if (student["enrollments"] is not JsonArray enrollments)
    {
        return;
    }

    foreach (var enrollment in enrollments.OfType<JsonObject>())
    {
        var courseCode = enrollment["courseCode"]?.GetValue<string>();
        CourseMetadataResponse? course = null;
        if (courseCode is not null)
        {
            courses.TryGetValue(courseCode, out course);
        }

        enrollment["credits"] = course?.CreditPoints;
        enrollment["courseTitle"] = course?.Name;
        enrollment["gradeType"] = course?.GradeType;
    }
}

static StudentStatisticsProfile ApplyCourseMetadata(
    StudentStatisticsProfile profile,
    IReadOnlyDictionary<string, CourseMetadataResponse> courses) =>
    profile with
    {
        Enrollments = profile.Enrollments?.Select(enrollment =>
        {
            if (courses.TryGetValue(enrollment.CourseCode, out var course))
            {
                return enrollment with
                {
                    CourseTitle = course.Name,
                    Credits = course.CreditPoints,
                    GradeType = course.GradeType
                };
            }

            return enrollment;
        }).ToList()
    };

public sealed record CourseMetadataResponse(string Code, string Name, int CreditPoints, string GradeType);

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

public sealed record StudentStatisticsProfile
{
    public string StudentId { get; init; } = string.Empty;
    public List<StudentStatisticsEnrollment>? Enrollments { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DegreeCode { get; init; }
    public string? Degree { get; init; }
    public string? Status { get; init; }
    public string? Major { get; init; }
    public string? SubMajor { get; init; }
}

public sealed record StudentStatisticsEnrollment
{
    public string CourseCode { get; init; } = string.Empty;
    public string? CourseTitle { get; init; }
    public string? TermCode { get; init; }
    public string? TermName { get; init; }
    public int AcademicYear { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? GradeType { get; init; }
    public decimal? Credits { get; init; }
    public decimal? GradeValue { get; init; }
    public bool IsFinalized { get; init; }
}

public sealed record StudentStatisticResponse(
    string StudentId,
    string Statistic,
    decimal? Value,
    int GradedEnrollmentCount,
    decimal TotalCredits);

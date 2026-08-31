using System.Net.Http.Json;

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

app.MapGet("/api/users", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    using var response = await clients.CreateClient("academicRecordsDatabase")
        .GetAsync("/api/users", cancellationToken);
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

app.MapControllers();

app.Run();

public sealed record UserResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? StudentId,
    string? StaffId);

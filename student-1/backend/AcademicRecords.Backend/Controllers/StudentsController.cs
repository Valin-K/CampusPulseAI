using System.Text.Json.Nodes;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api/students")]
public sealed class StudentsController(
    IHttpClientFactory clients,
    StudentCourseMetadataService courseMetadata) : AcademicRecordsControllerBase(clients)
{
    [HttpGet("by-user/{userId:int}")]
    public Task<IActionResult> GetByUser(int userId, CancellationToken cancellationToken) =>
        User.CanAccessUser(userId)
            ? ForwardStudentResponseAsync($"/api/students/by-user/{userId}", cancellationToken)
            : Task.FromResult<IActionResult>(Forbid());

    [HttpGet]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        ForwardStudentResponseAsync("/api/students", cancellationToken);

    [AllowAnonymous]
    [HttpPost("enroll")]
    public Task<IActionResult> Enroll(CancellationToken cancellationToken) =>
        ForwardJsonRequestAsync(AcademicRecordsCredentialsDatabase, HttpMethod.Post, "/api/students/enroll", cancellationToken);

    [HttpPost("{studentId}/enrollments")]
    public Task<IActionResult> AddEnrollment(string studentId, CancellationToken cancellationToken) =>
        User.CanAccessStudent(studentId)
            ? ForwardJsonRequestAsync(
            AcademicRecordsDatabase,
            HttpMethod.Post,
            $"/api/students/{Uri.EscapeDataString(studentId)}/enrollments",
            cancellationToken)
            : Task.FromResult<IActionResult>(Forbid());

    [HttpGet("{studentId}")]
    public Task<IActionResult> GetByStudentId(string studentId, CancellationToken cancellationToken) =>
        User.CanAccessStudent(studentId)
            ? ForwardStudentResponseAsync($"/api/students/{Uri.EscapeDataString(studentId)}", cancellationToken)
            : Task.FromResult<IActionResult>(Forbid());

    [HttpPut("{studentId}")]
    public Task<IActionResult> Update(string studentId, CancellationToken cancellationToken) =>
        User.CanAccessStudent(studentId)
            ? ForwardJsonRequestAsync(
            AcademicRecordsDatabase,
            HttpMethod.Put,
            $"/api/students/{Uri.EscapeDataString(studentId)}",
            cancellationToken)
            : Task.FromResult<IActionResult>(Forbid());

    [HttpPut("{studentId}/graduation")]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public Task<IActionResult> UpdateGraduation(string studentId, CancellationToken cancellationToken) =>
        ForwardJsonRequestAsync(
            AcademicRecordsDatabase,
            HttpMethod.Put,
            $"/api/students/{Uri.EscapeDataString(studentId)}/graduation",
            cancellationToken);

    private async Task<IActionResult> ForwardStudentResponseAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await AcademicRecordsDatabase.GetAsync(path, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        if (!response.IsSuccessStatusCode)
        {
            return new ContentResult
            {
                Content = content,
                ContentType = mediaType,
                StatusCode = (int)response.StatusCode
            };
        }

        var courses = await courseMetadata.GetCourseMetadataAsync(cancellationToken);
        var document = JsonNode.Parse(content);
        if (document is JsonArray students)
        {
            foreach (var student in students.OfType<JsonObject>())
            {
                StudentCourseMetadataService.AddCourseMetadata(student, courses);
            }
        }
        else if (document is JsonObject student)
        {
            StudentCourseMetadataService.AddCourseMetadata(student, courses);
        }

        return new ContentResult
        {
            Content = document?.ToJsonString() ?? content,
            ContentType = mediaType,
            StatusCode = (int)response.StatusCode
        };
    }

}

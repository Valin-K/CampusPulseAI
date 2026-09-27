using System.Net.Http.Json;
using AcademicRecords.Backend.Contracts;
using AcademicRecords.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api/students")]
public sealed class StudentAcademicController(
    IHttpClientFactory clients,
    StudentCourseMetadataService courseMetadata,
    GeminiAcademicProgressService progressService,
    ITranscriptPdfGenerator pdfGenerator) : AcademicRecordsControllerBase(clients)
{
    [HttpGet("{studentId}/wam")]
    public Task<IActionResult> GetWam(string studentId, CancellationToken cancellationToken) =>
        GetStudentStatisticAsync(studentId, false, cancellationToken);

    [HttpGet("{studentId}/gpa")]
    public Task<IActionResult> GetGpa(string studentId, CancellationToken cancellationToken) =>
        GetStudentStatisticAsync(studentId, true, cancellationToken);

    [HttpGet("{studentId}/statistics")]
    public async Task<IActionResult> GetStatistics(string studentId, CancellationToken cancellationToken)
    {
        var (profile, error) = await GetStudentProfileAsync(studentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var courses = await courseMetadata.GetCourseMetadataAsync(cancellationToken);
        profile = StudentCourseMetadataService.ApplyCourseMetadata(profile!, courses);
        return Ok(StudentAcademicStatisticsCalculator.Calculate(profile));
    }

    [HttpGet("{studentId}/eligibility")]
    public async Task<IActionResult> GetEligibility(string studentId, CancellationToken cancellationToken)
    {
        var (profile, error) = await GetStudentProfileAsync(studentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var courses = await courseMetadata.GetCourseMetadataAsync(cancellationToken);
        profile = StudentCourseMetadataService.ApplyCourseMetadata(profile!, courses);
        return Ok(GraduationEligibilityCalculator.Calculate(profile));
    }

    [HttpPost("{studentId}/ai-analysis")]
    public async Task<IActionResult> AnalyzeProgress(string studentId, CancellationToken cancellationToken)
    {
        var (profile, error) = await GetStudentProfileAsync(studentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var courses = await courseMetadata.GetCourseMetadataAsync(cancellationToken);
        var analysis = await progressService.AnalyzeAsync(
            StudentCourseMetadataService.ApplyCourseMetadata(profile!, courses),
            cancellationToken);
        return Ok(analysis);
    }

    [HttpGet("{studentId}/transcript.pdf")]
    public async Task<IActionResult> GetTranscript(string studentId, CancellationToken cancellationToken)
    {
        var (profile, error) = await GetStudentProfileAsync(studentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var courses = await courseMetadata.GetCourseMetadataAsync(cancellationToken);
        profile = StudentCourseMetadataService.ApplyCourseMetadata(profile!, courses);
        var pdf = pdfGenerator.Generate(TranscriptDataFactory.Create(profile, DateTime.Now));
        return File(pdf, "application/pdf", $"transcript-{profile.StudentId}.pdf");
    }

    private async Task<IActionResult> GetStudentStatisticAsync(
        string studentId,
        bool gpa,
        CancellationToken cancellationToken)
    {
        var (profile, error) = await GetStudentProfileAsync(studentId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var courses = await courseMetadata.GetCourseMetadataAsync(cancellationToken);
        profile = StudentCourseMetadataService.ApplyCourseMetadata(profile!, courses);

        var enrollments = StudentStatisticsCalculator.ToGradedEnrollments(profile.Enrollments);
        var statistic = gpa
            ? StudentStatisticsCalculator.CalculateGpa(enrollments)
            : StudentStatisticsCalculator.CalculateWam(enrollments);

        return Ok(new StudentStatisticResponse(profile.StudentId, gpa ? "GPA" : "WAM",
            statistic.Value, statistic.GradedEnrollmentCount, statistic.TotalCredits));
    }

    private async Task<(StudentStatisticsProfile? Profile, IActionResult? Error)> GetStudentProfileAsync(
        string studentId,
        CancellationToken cancellationToken)
    {
        using var response = await AcademicRecordsDatabase.GetAsync(
            $"/api/students/{Uri.EscapeDataString(studentId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return (null, new ContentResult
            {
                Content = content,
                ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/json",
                StatusCode = (int)response.StatusCode
            });
        }

        var profile = await response.Content.ReadFromJsonAsync<StudentStatisticsProfile>(cancellationToken);
        return profile is null
            ? (null, Problem("The student profile response was invalid."))
            : (profile, null);
    }
}

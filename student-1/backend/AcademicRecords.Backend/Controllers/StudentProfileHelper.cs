using System.Net.Http.Json;
using AcademicRecords.Backend.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

public static class StudentProfileHelper
{
    public static async Task<(StudentStatisticsProfile? Profile, IActionResult? Error)> GetStudentProfileAsync(
        this ControllerBase controller,
        HttpClient academicRecordsDatabase,
        string studentId,
        CancellationToken cancellationToken)
    {
        using var response = await academicRecordsDatabase.GetAsync(
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
            ? (null, controller.Problem("The student profile response was invalid."))
            : (profile, null);
    }
}

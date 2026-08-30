using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CourseManagement.Frontend.Models;

namespace CourseManagement.Frontend.Services;

public sealed class CourseApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<CourseSummary>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/courses", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<List<CourseSummary>>(cancellationToken)
            ?? [];
    }

    public async Task<CourseSummary> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/courses/{id}", cancellationToken);
        return await ReadCourseAsync(response, cancellationToken);
    }

    public async Task<CourseSummary> CreateAsync(
        CourseFormModel course,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/courses",
            course,
            cancellationToken);
        return await ReadCourseAsync(response, cancellationToken);
    }

    public async Task<CourseSummary> UpdateAsync(
        int id,
        CourseFormModel course,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/courses/{id}",
            course,
            cancellationToken);
        return await ReadCourseAsync(response, cancellationToken);
    }

    public async Task<CourseSummary> SetStatusAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/courses/{id}/status",
            new CourseStatusRequest(isActive),
            cancellationToken);
        return await ReadCourseAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"api/courses/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    static async Task<CourseSummary> ReadCourseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<CourseSummary>(cancellationToken)
            ?? throw new JsonException("The Course API returned an empty response.");
    }

    static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
        var validationMessages = problem?.Errors?
            .SelectMany(error => error.Value)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray() ?? [];

        var message = validationMessages.Length > 0
            ? string.Join(" ", validationMessages)
            : problem?.Detail
                ?? problem?.Title
                ?? $"The Course API returned HTTP {(int)response.StatusCode}.";

        throw new CourseApiException(response.StatusCode, message);
    }
}

public sealed class CourseApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

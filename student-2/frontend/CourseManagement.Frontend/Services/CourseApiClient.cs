using System.Net;
using System.Net.Http.Headers;
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

    public async Task<CourseDetail> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/courses/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<CourseDetail>(cancellationToken)
            ?? throw new JsonException("The Course API returned an empty response.");
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

    public async Task<SyllabusResponse> UploadSyllabusAsync(
        int courseId,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "file", fileName);

        using var response = await httpClient.PutAsync(
            $"api/courses/{courseId}/syllabus",
            form,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<SyllabusResponse>(cancellationToken)
            ?? throw new JsonException("The syllabus API returned an empty response.");
    }

    public async Task DeleteSyllabusAsync(
        int courseId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"api/courses/{courseId}/syllabus",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public string GetSyllabusUrl(int courseId, bool download = false)
    {
        var baseAddress = httpClient.BaseAddress
            ?? throw new InvalidOperationException("The Course API base address is not configured.");
        var relativeUrl = $"api/courses/{courseId}/syllabus";
        if (download)
        {
            relativeUrl += "?download=true";
        }

        return new Uri(baseAddress, relativeUrl).ToString();
    }

    public async Task<PrerequisiteSummary> GetPrerequisiteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"api/prerequisites/{id}",
            cancellationToken);
        return await ReadPrerequisiteAsync(response, cancellationToken);
    }

    public async Task<PrerequisiteSummary> CreatePrerequisiteAsync(
        PrerequisiteFormModel prerequisite,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/prerequisites",
            prerequisite,
            cancellationToken);
        return await ReadPrerequisiteAsync(response, cancellationToken);
    }

    public async Task<PrerequisiteSummary> UpdatePrerequisiteAsync(
        int id,
        PrerequisiteFormModel prerequisite,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/prerequisites/{id}",
            prerequisite,
            cancellationToken);
        return await ReadPrerequisiteAsync(response, cancellationToken);
    }

    public async Task DeletePrerequisiteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"api/prerequisites/{id}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<FacultyAssignmentSummary> GetFacultyAssignmentAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"api/faculty-assignments/{id}",
            cancellationToken);
        return await ReadFacultyAssignmentAsync(response, cancellationToken);
    }

    public async Task<FacultyAssignmentSummary> CreateFacultyAssignmentAsync(
        FacultyAssignmentFormModel assignment,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/faculty-assignments",
            assignment,
            cancellationToken);
        return await ReadFacultyAssignmentAsync(response, cancellationToken);
    }

    public async Task<FacultyAssignmentSummary> UpdateFacultyAssignmentAsync(
        int id,
        FacultyAssignmentFormModel assignment,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/faculty-assignments/{id}",
            assignment,
            cancellationToken);
        return await ReadFacultyAssignmentAsync(response, cancellationToken);
    }

    public async Task DeleteFacultyAssignmentAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"api/faculty-assignments/{id}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PrerequisiteEligibilityResult> CheckPrerequisiteEligibilityAsync(
        int courseId,
        PrerequisiteEligibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"api/courses/{courseId}/prerequisite-eligibility",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PrerequisiteEligibilityResult>(
                cancellationToken)
            ?? throw new JsonException("The prerequisite eligibility API returned an empty response.");
    }

    static async Task<CourseSummary> ReadCourseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<CourseSummary>(cancellationToken)
            ?? throw new JsonException("The Course API returned an empty response.");
    }

    static async Task<PrerequisiteSummary> ReadPrerequisiteAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PrerequisiteSummary>(cancellationToken)
            ?? throw new JsonException("The prerequisite API returned an empty response.");
    }

    static async Task<FacultyAssignmentSummary> ReadFacultyAssignmentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<FacultyAssignmentSummary>(
                cancellationToken)
            ?? throw new JsonException("The faculty assignment API returned an empty response.");
    }

    static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiProblemDetails? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

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

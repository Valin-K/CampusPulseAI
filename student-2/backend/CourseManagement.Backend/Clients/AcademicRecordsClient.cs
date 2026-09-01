using System.Net.Http.Json;
using System.Text.Json;

namespace CourseManagement.Backend.Clients;

public sealed class AcademicRecordsClient(HttpClient httpClient) : IAcademicRecordsClient
{
    public async Task<IReadOnlyList<AcademicRecordsUserResponse>> GetUsersAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/users", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AcademicRecordsResponseException(
                $"Academic Records returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<List<AcademicRecordsUserResponse>>(
                    cancellationToken)
                ?? throw new AcademicRecordsResponseException(
                    "Academic Records returned an empty user response.");
        }
        catch (JsonException exception)
        {
            throw new AcademicRecordsResponseException(
                "Academic Records returned an invalid user response.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new AcademicRecordsResponseException(
                "Academic Records returned an unsupported user response.",
                exception);
        }
    }

    public async Task<AcademicRecordsStudentResponse> GetStudentAsync(
        string studentId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"api/students/{Uri.EscapeDataString(studentId)}",
            cancellationToken);
        EnsureSuccess(response);

        try
        {
            return await response.Content.ReadFromJsonAsync<AcademicRecordsStudentResponse>(
                    cancellationToken)
                ?? throw new AcademicRecordsResponseException(
                    "Academic Records returned an empty Student response.");
        }
        catch (JsonException exception)
        {
            throw new AcademicRecordsResponseException(
                "Academic Records returned an invalid Student response.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new AcademicRecordsResponseException(
                "Academic Records returned an unsupported Student response.",
                exception);
        }
    }

    public async Task CreateEnrollmentAsync(
        string studentId,
        AcademicRecordsEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"api/students/{Uri.EscapeDataString(studentId)}/enrollments",
            request,
            cancellationToken);
        EnsureSuccess(response);
    }

    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new AcademicRecordsResponseException(
                $"Academic Records returned HTTP {(int)response.StatusCode}.");
        }
    }
}

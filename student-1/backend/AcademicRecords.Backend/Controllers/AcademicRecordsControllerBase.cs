using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

public abstract class AcademicRecordsControllerBase(IHttpClientFactory clients) : ControllerBase
{
    protected HttpClient AcademicRecordsDatabase => clients.CreateClient("academicRecordsDatabase");
    protected HttpClient AcademicRecordsCredentialsDatabase => clients.CreateClient("academicRecordsCredentials");
    protected HttpClient CourseManagementDatabase => clients.CreateClient("courseManagementDatabase");

    protected async Task<IActionResult> ForwardResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ContentResult
            {
                Content = content,
                ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/json",
                StatusCode = (int)response.StatusCode
            };
        }
    }

    protected async Task<IActionResult> ForwardJsonRequestAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StreamContent(Request.Body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.SendAsync(request, cancellationToken);
        return await ForwardResponseAsync(response, cancellationToken);
    }
}

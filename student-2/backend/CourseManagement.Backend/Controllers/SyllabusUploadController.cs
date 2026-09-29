using System.Net.Http.Headers;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CourseManagement.Backend.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/courses/{courseId:int}/syllabus")]
public sealed class SyllabusUploadController(
    IHttpClientFactory clients,
    IRagServerClient rag,
    ILogger<SyllabusUploadController> logger) : ControllerBase
{
    public const string RagClientName = "courseSyllabusRag";

    [HttpPut]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadOrReplace(
        int courseId,
        [FromForm(Name = "file")] IFormFile? file,
        CancellationToken cancellationToken)
    {
        byte[]? pdfBytes = null;
        string? fileName = null;
        using var form = new MultipartFormDataContent();
        if (file is not null)
        {
            await using var source = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            pdfBytes = buffer.ToArray();
            fileName = file.FileName;

            var fileContent = new ByteArrayContent(pdfBytes);
            if (MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType))
            {
                fileContent.Headers.ContentType = contentType;
            }

            form.Add(fileContent, "file", file.FileName);
        }

        try
        {
            using var databaseResponse = await clients
                .CreateClient(DatabaseApiProxy.ClientName)
                .PutAsync(
                    $"/api/courses/{courseId}/syllabus",
                    form,
                    cancellationToken);

            if (databaseResponse.IsSuccessStatusCode
                && pdfBytes is not null
                && fileName is not null)
            {
                await TryIngestAsync(
                    courseId,
                    fileName,
                    pdfBytes,
                    cancellationToken);
            }

            if (databaseResponse.Headers.Location is not null)
            {
                Response.Headers.Location = databaseResponse.Headers.Location.ToString();
            }

            return new ContentResult
            {
                StatusCode = (int)databaseResponse.StatusCode,
                Content = await databaseResponse.Content.ReadAsStringAsync(cancellationToken),
                ContentType = databaseResponse.Content.Headers.ContentType?.ToString()
                    ?? "application/json"
            };
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Course Management Database unavailable",
                detail: "The syllabus could not be stored because the Course database service is unavailable.");
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        int courseId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var databaseResponse = await clients
                .CreateClient(DatabaseApiProxy.ClientName)
                .DeleteAsync(
                    $"/api/courses/{courseId}/syllabus",
                    cancellationToken);

            if (databaseResponse.IsSuccessStatusCode)
            {
                await TryDeleteFromRagAsync(courseId, cancellationToken);
            }

            if (databaseResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return NoContent();
            }

            return new ContentResult
            {
                StatusCode = (int)databaseResponse.StatusCode,
                Content = await databaseResponse.Content.ReadAsStringAsync(cancellationToken),
                ContentType = databaseResponse.Content.Headers.ContentType?.ToString()
                    ?? "application/json"
            };
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Course Management Database unavailable",
                detail: "The syllabus could not be deleted because the Course database service is unavailable.");
        }
    }

    async Task TryIngestAsync(
        int courseId,
        string fileName,
        byte[] pdfBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var pdfStream = new MemoryStream(pdfBytes, writable: false);
            using var pdf = PdfDocument.Open(pdfStream);
            var text = string.Join(
                Environment.NewLine,
                pdf.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await rag.IngestAsync(
                "coursemanagement",
                new RagIngestRequest(
                [
                    new RagDocument(
                        $"course-{courseId}-syllabus",
                        text,
                        new Dictionary<string, string>
                        {
                            ["courseId"] = courseId.ToString(),
                            ["title"] = fileName,
                            ["source"] = "syllabus"
                        })
                ]),
                cancellationToken);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
        }
    }

    async Task TryDeleteFromRagAsync(
        int courseId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients
                .CreateClient(RagClientName)
                .DeleteAsync(
                    $"/api/rag/coursemanagement/documents/course-{courseId}-syllabus",
                    cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{ErrorMessage}", exception.Message);
        }
    }
}

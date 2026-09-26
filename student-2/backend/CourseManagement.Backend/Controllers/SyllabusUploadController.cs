using System.Net.Http.Headers;
using System.Text.Json;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CourseManagement.Backend.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
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
        using var form = new MultipartFormDataContent();
        if (file is not null)
        {
            await using var source = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            pdfBytes = buffer.ToArray();

            var fileContent = new ByteArrayContent(pdfBytes);
            if (MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType))
            {
                fileContent.Headers.ContentType = contentType;
            }

            form.Add(fileContent, "file", file.FileName);
        }

        HttpResponseMessage databaseResponse;
        try
        {
            databaseResponse = await clients
                .CreateClient(DatabaseApiProxy.ClientName)
                .PutAsync(
                    $"/api/courses/{courseId}/syllabus",
                    form,
                    cancellationToken);
        }
        catch (HttpRequestException)
        {
            return DatabaseUnavailable();
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return DatabaseUnavailable();
        }

        using (databaseResponse)
        {
            if (databaseResponse.IsSuccessStatusCode && pdfBytes is not null)
            {
                await TryIngestAsync(
                    courseId,
                    file!.FileName,
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
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        int courseId,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage databaseResponse;
        try
        {
            databaseResponse = await clients
                .CreateClient(DatabaseApiProxy.ClientName)
                .DeleteAsync(
                    $"/api/courses/{courseId}/syllabus",
                    cancellationToken);
        }
        catch (HttpRequestException)
        {
            return DatabaseUnavailable();
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return DatabaseUnavailable();
        }

        using (databaseResponse)
        {
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
                logger.LogWarning(
                    "The syllabus for Course {CourseId} was stored but contained no extractable text for RAG ingestion.",
                    courseId);
                return;
            }

            var response = await rag.IngestAsync(
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

            if (response is null)
            {
                logger.LogWarning(
                    "The syllabus for Course {CourseId} was stored but the RAG server returned an empty ingestion response.",
                    courseId);
            }
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "RAG ingestion timed out for the syllabus belonging to Course {CourseId}.",
                courseId);
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or IOException
                or InvalidOperationException
                or JsonException
                or NotSupportedException
                or PdfDocumentFormatException)
        {
            logger.LogWarning(
                exception,
                "The syllabus for Course {CourseId} was stored but RAG ingestion failed.",
                courseId);
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

            if (response.Content.Headers.ContentLength == 0)
            {
                logger.LogWarning(
                    "The syllabus for Course {CourseId} was deleted but the RAG server returned an empty deletion response.",
                    courseId);
            }
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "RAG deletion timed out for the syllabus belonging to Course {CourseId}.",
                courseId);
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or InvalidOperationException
                or JsonException
                or NotSupportedException)
        {
            logger.LogWarning(
                exception,
                "The syllabus for Course {CourseId} was deleted but RAG cleanup failed.",
                courseId);
        }
    }

    ObjectResult DatabaseUnavailable() =>
        Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Course Management Database unavailable",
            detail: "The syllabus could not be stored because the Course database service is unavailable.");
}

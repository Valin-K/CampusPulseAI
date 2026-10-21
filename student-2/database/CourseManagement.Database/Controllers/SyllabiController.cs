using CourseManagement.Backend.Contracts.Syllabi;
using CourseManagement.Backend.Services;
using CourseManagement.Database.Data;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace CourseManagement.Backend.Controllers;

[ApiController]
[Route("api/courses/{courseId:int}/syllabus")]
public sealed class SyllabiController(
    CourseManagementDbContext database,
    ISyllabusStorage storage) : ControllerBase
{
    const string PdfContentType = "application/pdf";
    const int MaxFileNameLength = 260;

    [HttpPut]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<SyllabusResponse>> UploadOrReplace(
        int courseId,
        [FromForm(Name = "file")] IFormFile? file,
        CancellationToken cancellationToken)
    {
        var course = await database.Courses.FindAsync([courseId], cancellationToken);
        if (course is null)
        {
            return CourseNotFound(courseId);
        }

        var fileNameValidation = ValidateFile(file);
        if (fileNameValidation.ErrorResult is not null)
        {
            return fileNameValidation.ErrorResult;
        }

        var safeFileName = fileNameValidation.FileName!;
        var isReplacement = !string.IsNullOrWhiteSpace(course.SyllabusFileName);
        var previousFileName = course.SyllabusFileName;

        try
        {
            await using var source = file!.OpenReadStream();
            await using var stagedFile = await storage.StageReplacementAsync(
                courseId,
                source,
                cancellationToken);

            course.SyllabusFileName = safeFileName;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                course.SyllabusFileName = previousFileName;
                return StorageProblem(
                    "The syllabus metadata could not be saved. The previous syllabus was preserved.");
            }

            await stagedFile.CommitAsync();

            var response = new SyllabusResponse(
                courseId,
                safeFileName,
                PdfContentType,
                file.Length);

            return isReplacement
                ? Ok(response)
                : CreatedAtAction(nameof(Get), new { courseId }, response);
        }
        catch (SyllabusFileTooLargeException exception)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                CreateProblem(
                    StatusCodes.Status413PayloadTooLarge,
                    "Syllabus PDF is too large",
                    exception.Message));
        }
        catch (InvalidSyllabusFileException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (SyllabusStorageException)
        {
            return StorageProblem("The syllabus could not be stored. The previous syllabus was preserved.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        int courseId,
        [FromQuery] bool download = false,
        CancellationToken cancellationToken = default)
    {
        var course = await database.Courses
            .AsNoTracking()
            .Where(item => item.Id == courseId)
            .Select(item => new { item.Id, item.SyllabusFileName })
            .SingleOrDefaultAsync(cancellationToken);

        if (course is null)
        {
            return CourseNotFound(courseId);
        }

        if (string.IsNullOrWhiteSpace(course.SyllabusFileName))
        {
            return SyllabusNotFound(courseId);
        }

        StoredSyllabusFile? storedFile = null;
        try
        {
            storedFile = await storage.OpenReadAsync(courseId, cancellationToken);
            if (storedFile is null)
            {
                var currentCourse = await database.Courses
                    .AsNoTracking()
                    .Where(item => item.Id == courseId)
                    .Select(item => new { item.SyllabusFileName })
                    .SingleOrDefaultAsync(cancellationToken);

                return currentCourse is null
                    ? CourseNotFound(courseId)
                    : string.IsNullOrWhiteSpace(currentCourse.SyllabusFileName)
                        ? SyllabusNotFound(courseId)
                        : StorageProblem(
                            "The syllabus metadata exists, but the stored PDF is unavailable.");
            }

            var currentFileName = await database.Courses
                .AsNoTracking()
                .Where(item => item.Id == courseId)
                .Select(item => item.SyllabusFileName)
                .SingleOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(currentFileName))
            {
                await storedFile.DisposeAsync();
                storedFile = null;
                return SyllabusNotFound(courseId);
            }

            var disposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline")
            {
                FileNameStar = currentFileName
            };
            Response.GetTypedHeaders().ContentDisposition = disposition;
            Response.Headers.XContentTypeOptions = "nosniff";

            var result = File(
                storedFile.Content,
                PdfContentType,
                enableRangeProcessing: true);
            storedFile.ReleaseLock();
            storedFile = null;
            return result;
        }
        catch (SyllabusStorageException)
        {
            return StorageProblem("The syllabus PDF could not be read.");
        }
        finally
        {
            if (storedFile is not null)
            {
                await storedFile.DisposeAsync();
            }
        }
    }

    [HttpDelete]
    [Authorize(Policy = CampusPulseSecurityExtensions.StaffOnlyPolicy)]
    public async Task<IActionResult> Delete(
        int courseId,
        CancellationToken cancellationToken)
    {
        var course = await database.Courses.FindAsync([courseId], cancellationToken);
        if (course is null)
        {
            return CourseNotFound(courseId);
        }

        if (string.IsNullOrWhiteSpace(course.SyllabusFileName))
        {
            return SyllabusNotFound(courseId);
        }

        try
        {
            await using var stagedFile = await storage.StageDeletionAsync(
                courseId,
                cancellationToken);

            var previousFileName = course.SyllabusFileName;
            course.SyllabusFileName = null;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                course.SyllabusFileName = previousFileName;
                return StorageProblem(
                    "The syllabus metadata could not be removed. The PDF was preserved.");
            }

            if (stagedFile is not null)
            {
                await stagedFile.CommitAsync();
            }

            return NoContent();
        }
        catch (SyllabusStorageException)
        {
            return StorageProblem("The syllabus could not be removed.");
        }
    }

    (string? FileName, ActionResult? ErrorResult) ValidateFile(IFormFile? file)
    {
        if (file is null)
        {
            return (null, BadRequestProblem("A syllabus PDF file is required."));
        }

        var fileName = file.FileName.Trim();
        if (fileName.Length == 0)
        {
            return (null, BadRequestProblem("The syllabus filename is required."));
        }

        if (fileName.Length > MaxFileNameLength)
        {
            return (null, BadRequestProblem(
                $"The syllabus filename must not exceed {MaxFileNameLength} characters."));
        }

        if (fileName is "." or ".."
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.Any(char.IsControl))
        {
            return (null, BadRequestProblem(
                "The syllabus filename must not contain path separators or control characters."));
        }

        if (!string.Equals(
                Path.GetExtension(fileName),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return (null, BadRequestProblem("The syllabus file must use the .pdf extension."));
        }

        if (!string.Equals(file.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
        {
            return (null, BadRequestProblem(
                "The syllabus file content type must be application/pdf."));
        }

        if (file.Length == 0)
        {
            return (null, BadRequestProblem("The syllabus PDF is empty."));
        }

        if (file.Length > storage.MaxFileSizeBytes)
        {
            return (null, StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                CreateProblem(
                    StatusCodes.Status413PayloadTooLarge,
                    "Syllabus PDF is too large",
                    $"The syllabus PDF must not exceed {storage.MaxFileSizeBytes / 1024 / 1024} MiB.")));
        }

        return (fileName, null);
    }

    NotFoundObjectResult CourseNotFound(int courseId) =>
        NotFound(CreateProblem(
            StatusCodes.Status404NotFound,
            "Course not found",
            $"Course {courseId} does not exist."));

    NotFoundObjectResult SyllabusNotFound(int courseId) =>
        NotFound(CreateProblem(
            StatusCodes.Status404NotFound,
            "Syllabus not found",
            $"Course {courseId} does not have a syllabus PDF."));

    BadRequestObjectResult BadRequestProblem(string detail) =>
        BadRequest(CreateProblem(
            StatusCodes.Status400BadRequest,
            "Invalid syllabus PDF",
            detail));

    ObjectResult StorageProblem(string detail) =>
        StatusCode(
            StatusCodes.Status500InternalServerError,
            CreateProblem(
                StatusCodes.Status500InternalServerError,
                "Syllabus storage error",
                detail));

    static ProblemDetails CreateProblem(int status, string title, string detail) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail
        };
}

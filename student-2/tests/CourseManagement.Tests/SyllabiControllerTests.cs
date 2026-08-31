using System.Text;
using CourseManagement.Backend.Contracts.Syllabi;
using CourseManagement.Backend.Controllers;
using CourseManagement.Backend.Services;
using CourseManagement.Database.Data;
using CourseManagement.Database.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace CourseManagement.Tests;

public sealed class SyllabiControllerTests
{
    [Test]
    public async Task UploadStoresPdfAndMetadata()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        var pdf = CreatePdf("version one");

        var result = await context.Syllabi.UploadOrReplace(
            course.Id,
            CreateUpload(pdf, "Course Outline.pdf"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var created = (CreatedAtActionResult)result.Result;
        Assert.That(created.Value, Is.TypeOf<SyllabusResponse>());
        var response = (SyllabusResponse)created.Value;
        Assert.Multiple(() =>
        {
            Assert.That(response.FileName, Is.EqualTo("Course Outline.pdf"));
            Assert.That(response.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(response.SizeBytes, Is.EqualTo(pdf.Length));
            Assert.That(course.SyllabusFileName, Is.EqualTo("Course Outline.pdf"));
            Assert.That(
                File.ReadAllBytes(context.Storage.GetStoredPath(course.Id)),
                Is.EqualTo(pdf));
        });
    }

    [TestCase("../outline.pdf")]
    [TestCase(@"folder\outline.pdf")]
    [TestCase("outline.txt")]
    [TestCase("outline\r\n.pdf")]
    public async Task UploadRejectsUnsafeOrNonPdfFilename(string fileName)
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");

        var result = await context.Syllabi.UploadOrReplace(
            course.Id,
            CreateUpload(CreatePdf("content"), fileName),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That(course.SyllabusFileName, Is.Null);
        Assert.That(File.Exists(context.Storage.GetStoredPath(course.Id)), Is.False);
    }

    [Test]
    public async Task UploadRejectsIncorrectContentType()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");

        var result = await context.Syllabi.UploadOrReplace(
            course.Id,
            CreateUpload(CreatePdf("content"), "outline.pdf", "text/plain"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UploadRejectsFileWithoutPdfSignature()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");

        var result = await context.Syllabi.UploadOrReplace(
            course.Id,
            CreateUpload(Encoding.UTF8.GetBytes("not a PDF"), "outline.pdf"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That(course.SyllabusFileName, Is.Null);
    }

    [Test]
    public async Task UploadRejectsFileAboveConfiguredLimit()
    {
        await using var context = await SyllabusTestContext.CreateAsync(maxFileSizeBytes: 8);
        var course = await context.AddCourseAsync("CPAI5000");

        var result = await context.Syllabi.UploadOrReplace(
            course.Id,
            CreateUpload(CreatePdf("content"), "outline.pdf"),
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
    }

    [Test]
    public async Task UploadReturnsNotFoundForUnknownCourse()
    {
        await using var context = await SyllabusTestContext.CreateAsync();

        var result = await context.Syllabi.UploadOrReplace(
            999,
            CreateUpload(CreatePdf("content"), "outline.pdf"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task ReplacementUpdatesBytesAndFilename()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        await context.UploadAsync(course.Id, CreatePdf("version one"), "v1.pdf");
        var replacement = CreatePdf("version two");

        var result = await context.Syllabi.UploadOrReplace(
            course.Id,
            CreateUpload(replacement, "v2.pdf"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.Multiple(() =>
        {
            Assert.That(course.SyllabusFileName, Is.EqualTo("v2.pdf"));
            Assert.That(
                File.ReadAllBytes(context.Storage.GetStoredPath(course.Id)),
                Is.EqualTo(replacement));
        });
    }

    [Test]
    public async Task GetReturnsPdfWithInlineDisposition()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        var pdf = CreatePdf("view content");
        await context.UploadAsync(course.Id, pdf, "Course Outline.pdf");

        var result = await context.Syllabi.Get(
            course.Id,
            download: false,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<FileStreamResult>());
        var fileResult = (FileStreamResult)result;
        await using var returnedStream = fileResult.FileStream;
        using var returnedContent = new MemoryStream();
        await returnedStream.CopyToAsync(returnedContent);

        Assert.Multiple(() =>
        {
            Assert.That(fileResult.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(returnedContent.ToArray(), Is.EqualTo(pdf));
            Assert.That(
                context.Syllabi.Response.GetTypedHeaders().ContentDisposition?.DispositionType,
                Is.EqualTo("inline"));
            Assert.That(
                context.Syllabi.Response.Headers.XContentTypeOptions.ToString(),
                Is.EqualTo("nosniff"));
        });
    }

    [Test]
    public async Task GetUsesAttachmentDispositionForDownload()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        await context.UploadAsync(course.Id, CreatePdf("download content"), "outline.pdf");

        var result = await context.Syllabi.Get(
            course.Id,
            download: true,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<FileStreamResult>());
        var fileResult = (FileStreamResult)result;
        await fileResult.FileStream.DisposeAsync();
        Assert.That(
            context.Syllabi.Response.GetTypedHeaders().ContentDisposition?.DispositionType,
            Is.EqualTo("attachment"));
    }

    [Test]
    public async Task GetReturnsNotFoundWhenCourseHasNoSyllabus()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");

        var result = await context.Syllabi.Get(
            course.Id,
            cancellationToken: CancellationToken.None);

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task DeleteRemovesFileAndMetadata()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        await context.UploadAsync(course.Id, CreatePdf("content"), "outline.pdf");

        var result = await context.Syllabi.Delete(course.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<NoContentResult>());
            Assert.That(course.SyllabusFileName, Is.Null);
            Assert.That(File.Exists(context.Storage.GetStoredPath(course.Id)), Is.False);
        });
    }

    [Test]
    public async Task DeleteClearsMetadataWhenStoredFileIsAlreadyMissing()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        await context.UploadAsync(course.Id, CreatePdf("content"), "outline.pdf");
        File.Delete(context.Storage.GetStoredPath(course.Id));

        var result = await context.Syllabi.Delete(course.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<NoContentResult>());
            Assert.That(course.SyllabusFileName, Is.Null);
        });
    }

    [Test]
    public async Task CourseDeletionRemovesSyllabus()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        await context.UploadAsync(course.Id, CreatePdf("content"), "outline.pdf");

        var result = await context.Courses.Delete(course.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<NoContentResult>());
            Assert.That(context.Database.Courses.Any(), Is.False);
            Assert.That(File.Exists(context.Storage.GetStoredPath(course.Id)), Is.False);
        });
    }

    [Test]
    public async Task CourseDeletionSucceedsWhenStoredFileIsAlreadyMissing()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var course = await context.AddCourseAsync("CPAI5000");
        await context.UploadAsync(course.Id, CreatePdf("content"), "outline.pdf");
        File.Delete(context.Storage.GetStoredPath(course.Id));

        var result = await context.Courses.Delete(course.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<NoContentResult>());
            Assert.That(context.Database.Courses.Any(), Is.False);
        });
    }

    [Test]
    public async Task BlockedCourseDeletionPreservesSyllabus()
    {
        await using var context = await SyllabusTestContext.CreateAsync();
        var requiredCourse = await context.AddCourseAsync("CPAI1000");
        var dependentCourse = await context.AddCourseAsync("CPAI2000");
        context.Database.Prerequisites.Add(new Prerequisite
        {
            CourseId = dependentCourse.Id,
            RequiredCourseId = requiredCourse.Id,
            MinimumGrade = "Pass"
        });
        await context.Database.SaveChangesAsync();
        var pdf = CreatePdf("content");
        await context.UploadAsync(requiredCourse.Id, pdf, "outline.pdf");

        var result = await context.Courses.Delete(
            requiredCourse.Id,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<ConflictObjectResult>());
            Assert.That(
                File.ReadAllBytes(context.Storage.GetStoredPath(requiredCourse.Id)),
                Is.EqualTo(pdf));
            Assert.That(requiredCourse.SyllabusFileName, Is.EqualTo("outline.pdf"));
        });
    }

    [Test]
    public async Task UncommittedReplacementRestoresPreviousFile()
    {
        await using var storage = TestSyllabusStorage.Create();
        var original = CreatePdf("original");
        await using (var originalStream = new MemoryStream(original))
        await using (var initialChange = await storage.StageReplacementAsync(
                         42,
                         originalStream,
                         CancellationToken.None))
        {
            await initialChange.CommitAsync();
        }

        await using (var replacementStream = new MemoryStream(CreatePdf("replacement")))
        {
            await using var replacementChange = await storage.StageReplacementAsync(
                42,
                replacementStream,
                CancellationToken.None);
        }

        Assert.That(
            File.ReadAllBytes(storage.GetStoredPath(42)),
            Is.EqualTo(original));
    }

    [Test]
    public async Task ConcurrentReplacementsAreSerialized()
    {
        await using var storage = TestSyllabusStorage.Create();
        var firstPdf = CreatePdf("first");
        var secondPdf = CreatePdf("second");

        await using var firstStream = new MemoryStream(firstPdf);
        await using var firstChange = await storage.StageReplacementAsync(
            42,
            firstStream,
            CancellationToken.None);

        await using var secondStream = new MemoryStream(secondPdf);
        var secondChangeTask = storage.StageReplacementAsync(
            42,
            secondStream,
            CancellationToken.None);

        await Task.Delay(50);
        Assert.That(secondChangeTask.IsCompleted, Is.False);

        await firstChange.CommitAsync();
        await using var secondChange = await secondChangeTask;
        await secondChange.CommitAsync();

        Assert.That(
            File.ReadAllBytes(storage.GetStoredPath(42)),
            Is.EqualTo(secondPdf));
    }

    [Test]
    public async Task ReadWaitsForStagedDeletion()
    {
        await using var storage = TestSyllabusStorage.Create();
        await using (var source = new MemoryStream(CreatePdf("content")))
        await using (var upload = await storage.StageReplacementAsync(
                         42,
                         source,
                         CancellationToken.None))
        {
            await upload.CommitAsync();
        }

        await using var deletion = await storage.StageDeletionAsync(
            42,
            CancellationToken.None);
        Assert.That(deletion, Is.Not.Null);

        var readTask = storage.OpenReadAsync(42, CancellationToken.None);
        await Task.Delay(50);
        Assert.That(readTask.IsCompleted, Is.False);

        await deletion!.CommitAsync();
        var storedFile = await readTask;

        Assert.That(storedFile, Is.Null);
    }

    [Test]
    public async Task ReadLeaseBlocksReplacementUntilReleased()
    {
        await using var storage = TestSyllabusStorage.Create();
        await using (var source = new MemoryStream(CreatePdf("original")))
        await using (var upload = await storage.StageReplacementAsync(
                         42,
                         source,
                         CancellationToken.None))
        {
            await upload.CommitAsync();
        }

        await using var storedFile = await storage.OpenReadAsync(
            42,
            CancellationToken.None);
        Assert.That(storedFile, Is.Not.Null);

        await using var replacementStream = new MemoryStream(CreatePdf("replacement"));
        var replacementTask = storage.StageReplacementAsync(
            42,
            replacementStream,
            CancellationToken.None);
        await Task.Delay(50);
        Assert.That(replacementTask.IsCompleted, Is.False);

        storedFile!.ReleaseLock();
        await using var replacement = await replacementTask;
        await replacement.CommitAsync();

        Assert.That(
            File.ReadAllBytes(storage.GetStoredPath(42)),
            Is.EqualTo(CreatePdf("replacement")));
    }

    static FormFile CreateUpload(
        byte[] content,
        string fileName,
        string contentType = "application/pdf")
    {
        return new FormFile(
            new MemoryStream(content),
            0,
            content.Length,
            "file",
            fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    static byte[] CreatePdf(string content) =>
        Encoding.ASCII.GetBytes($"%PDF-1.7\n{content}\n%%EOF");

    sealed class SyllabusTestContext(
        SqliteConnection connection,
        CourseManagementDbContext database,
        TestSyllabusStorage storage) : IAsyncDisposable
    {
        public CourseManagementDbContext Database { get; } = database;
        public TestSyllabusStorage Storage { get; } = storage;
        public SyllabiController Syllabi { get; } = CreateSyllabiController(database, storage);
        public CoursesController Courses { get; } = new(database, storage);

        public static async Task<SyllabusTestContext> CreateAsync(
            long maxFileSizeBytes = SyllabusStorageOptions.DefaultMaxFileSizeBytes)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CourseManagementDbContext>()
                .UseSqlite(connection)
                .Options;
            var database = new CourseManagementDbContext(options);
            await database.Database.EnsureCreatedAsync();

            return new SyllabusTestContext(
                connection,
                database,
                TestSyllabusStorage.Create(maxFileSizeBytes));
        }

        public async Task<Course> AddCourseAsync(string code)
        {
            var course = new Course
            {
                Code = code,
                Name = $"{code} Course",
                Description = "Course created by a syllabus controller test.",
                CreditPoints = 6,
                Level = 1,
                IsActive = true
            };

            Database.Courses.Add(course);
            await Database.SaveChangesAsync();
            return course;
        }

        public async Task UploadAsync(int courseId, byte[] content, string fileName)
        {
            var result = await Syllabi.UploadOrReplace(
                courseId,
                CreateUpload(content, fileName),
                CancellationToken.None);
            Assert.That(
                result.Result,
                Is.InstanceOf<CreatedAtActionResult>().Or.InstanceOf<OkObjectResult>());
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
            await Storage.DisposeAsync();
        }

        static SyllabiController CreateSyllabiController(
            CourseManagementDbContext database,
            ISyllabusStorage storage)
        {
            return new SyllabiController(database, storage)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }
    }
}

using CourseManagement.Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CourseManagement.Tests;

internal sealed class TestSyllabusStorage : ISyllabusStorage, IAsyncDisposable
{
    readonly FileSystemSyllabusStorage storage;

    TestSyllabusStorage(string rootPath, long maxFileSizeBytes)
    {
        RootPath = rootPath;
        storage = new FileSystemSyllabusStorage(
            Options.Create(new SyllabusStorageOptions
            {
                RootPath = rootPath,
                MaxFileSizeBytes = maxFileSizeBytes
            }),
            NullLogger<FileSystemSyllabusStorage>.Instance);
    }

    public string RootPath { get; }
    public long MaxFileSizeBytes => storage.MaxFileSizeBytes;

    public static TestSyllabusStorage Create(
        long maxFileSizeBytes = SyllabusStorageOptions.DefaultMaxFileSizeBytes)
    {
        var directory = Directory.CreateTempSubdirectory("campuspulse-syllabi-");
        return new TestSyllabusStorage(directory.FullName, maxFileSizeBytes);
    }

    public string GetStoredPath(int courseId) =>
        Path.Combine(RootPath, $"course-{courseId}.pdf");

    public Task<IStagedSyllabusChange> StageReplacementAsync(
        int courseId,
        Stream content,
        CancellationToken cancellationToken) =>
        storage.StageReplacementAsync(courseId, content, cancellationToken);

    public Task<IStagedSyllabusChange?> StageDeletionAsync(
        int courseId,
        CancellationToken cancellationToken) =>
        storage.StageDeletionAsync(courseId, cancellationToken);

    public Task<StoredSyllabusFile?> OpenReadAsync(
        int courseId,
        CancellationToken cancellationToken) =>
        storage.OpenReadAsync(courseId, cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}

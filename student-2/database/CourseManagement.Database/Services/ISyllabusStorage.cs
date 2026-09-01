namespace CourseManagement.Backend.Services;

public interface ISyllabusStorage
{
    long MaxFileSizeBytes { get; }

    Task<IStagedSyllabusChange> StageReplacementAsync(
        int courseId,
        Stream content,
        CancellationToken cancellationToken);

    Task<IStagedSyllabusChange?> StageDeletionAsync(
        int courseId,
        CancellationToken cancellationToken);

    Task<StoredSyllabusFile?> OpenReadAsync(
        int courseId,
        CancellationToken cancellationToken);
}

public interface IStagedSyllabusChange : IAsyncDisposable
{
    ValueTask CommitAsync();
}

public sealed class StoredSyllabusFile(
    Stream content,
    long length,
    Action releaseMutationLock) : IAsyncDisposable
{
    Action? releaseLock = releaseMutationLock;

    public Stream Content { get; } = content;
    public long Length { get; } = length;

    public void ReleaseLock() =>
        Interlocked.Exchange(ref releaseLock, null)?.Invoke();

    public async ValueTask DisposeAsync()
    {
        ReleaseLock();
        await Content.DisposeAsync();
    }
}

public sealed class InvalidSyllabusFileException(string message) : Exception(message);

public sealed class SyllabusFileTooLargeException(long maxFileSizeBytes)
    : Exception($"The syllabus PDF must not exceed {maxFileSizeBytes / 1024 / 1024} MiB.");

public sealed class SyllabusStorageException(string message, Exception innerException)
    : Exception(message, innerException);

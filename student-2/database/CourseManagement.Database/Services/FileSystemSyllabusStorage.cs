using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace CourseManagement.Backend.Services;

public sealed class FileSystemSyllabusStorage(
    IOptions<SyllabusStorageOptions> options,
    ILogger<FileSystemSyllabusStorage> logger) : ISyllabusStorage
{
    static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    readonly string rootPath = Path.GetFullPath(options.Value.RootPath);
    readonly ConcurrentDictionary<int, SemaphoreSlim> mutationLocks = new();

    public long MaxFileSizeBytes { get; } = options.Value.MaxFileSizeBytes;

    public async Task<IStagedSyllabusChange> StageReplacementAsync(
        int courseId,
        Stream content,
        CancellationToken cancellationToken)
    {
        var finalPath = GetSyllabusPath(courseId);
        var operationId = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(rootPath, $".course-{courseId}-{operationId}.upload");
        var backupPath = Path.Combine(rootPath, $".course-{courseId}-{operationId}.backup");
        var backupCreated = false;
        var replacementInstalled = false;
        var mutationLock = mutationLocks.GetOrAdd(courseId, _ => new SemaphoreSlim(1, 1));
        var lockTransferred = false;

        await mutationLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(rootPath);
            await WriteValidatedPdfAsync(content, temporaryPath, cancellationToken);

            if (File.Exists(finalPath))
            {
                File.Move(finalPath, backupPath);
                backupCreated = true;
            }

            File.Move(temporaryPath, finalPath);
            replacementInstalled = true;

            var change = new StagedSyllabusChange(
                finalPath,
                backupCreated ? backupPath : null,
                replacementInstalled: true,
                mutationLock,
                logger);
            lockTransferred = true;
            return change;
        }
        catch (InvalidSyllabusFileException)
        {
            throw;
        }
        catch (SyllabusFileTooLargeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not stage a syllabus replacement for Course {CourseId}.", courseId);
            throw new SyllabusStorageException(
                "The syllabus file could not be stored.",
                exception);
        }
        finally
        {
            TryDelete(temporaryPath);

            if (!replacementInstalled && backupCreated)
            {
                TryRestore(backupPath, finalPath);
            }

            if (!lockTransferred)
            {
                mutationLock.Release();
            }
        }
    }

    public async Task<IStagedSyllabusChange?> StageDeletionAsync(
        int courseId,
        CancellationToken cancellationToken)
    {
        var mutationLock = mutationLocks.GetOrAdd(courseId, _ => new SemaphoreSlim(1, 1));
        await mutationLock.WaitAsync(cancellationToken);
        var finalPath = GetSyllabusPath(courseId);
        if (!File.Exists(finalPath))
        {
            logger.LogWarning(
                "No stored syllabus PDF was found for Course {CourseId}; deletion will treat it as already absent.",
                courseId);
            mutationLock.Release();
            return null;
        }

        var backupPath = Path.Combine(
            rootPath,
            $".course-{courseId}-{Guid.NewGuid():N}.deleting");

        try
        {
            File.Move(finalPath, backupPath);
            return new StagedSyllabusChange(
                finalPath,
                backupPath,
                replacementInstalled: false,
                mutationLock,
                logger);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            mutationLock.Release();
            logger.LogError(exception, "Could not stage syllabus deletion for Course {CourseId}.", courseId);
            throw new SyllabusStorageException(
                "The syllabus file could not be removed.",
                exception);
        }
    }

    public async Task<StoredSyllabusFile?> OpenReadAsync(
        int courseId,
        CancellationToken cancellationToken)
    {
        var mutationLock = mutationLocks.GetOrAdd(courseId, _ => new SemaphoreSlim(1, 1));
        await mutationLock.WaitAsync(cancellationToken);
        var path = GetSyllabusPath(courseId);
        if (!File.Exists(path))
        {
            mutationLock.Release();
            return null;
        }

        try
        {
            var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read | FileShare.Delete,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            return new StoredSyllabusFile(
                stream,
                stream.Length,
                () => mutationLock.Release());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            mutationLock.Release();
            logger.LogError(exception, "Could not read the syllabus for Course {CourseId}.", courseId);
            throw new SyllabusStorageException(
                "The syllabus file could not be read.",
                exception);
        }
    }

    async Task WriteValidatedPdfAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var signature = new byte[PdfSignature.Length];
        var signatureLength = 0;
        var totalBytes = 0L;
        var buffer = new byte[81920];

        await using var destination = new FileStream(
            destinationPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = buffer.Length,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > MaxFileSizeBytes)
            {
                throw new SyllabusFileTooLargeException(MaxFileSizeBytes);
            }

            if (signatureLength < signature.Length)
            {
                var bytesToCopy = Math.Min(signature.Length - signatureLength, bytesRead);
                buffer.AsSpan(0, bytesToCopy).CopyTo(signature.AsSpan(signatureLength));
                signatureLength += bytesToCopy;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (totalBytes == 0)
        {
            throw new InvalidSyllabusFileException("The syllabus PDF is empty.");
        }

        if (signatureLength < PdfSignature.Length
            || !signature.AsSpan().SequenceEqual(PdfSignature))
        {
            throw new InvalidSyllabusFileException(
                "The selected file does not contain a valid PDF signature.");
        }

        await destination.FlushAsync(cancellationToken);
    }

    string GetSyllabusPath(int courseId) =>
        Path.Combine(rootPath, $"course-{courseId}.pdf");

    void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not clean up temporary syllabus file {FileName}.", Path.GetFileName(path));
        }
    }

    void TryRestore(string backupPath, string finalPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                if (File.Exists(finalPath))
                {
                    logger.LogError(
                        "Could not restore syllabus backup {FileName} because the destination already exists.",
                        Path.GetFileName(backupPath));
                    return;
                }

                File.Move(backupPath, finalPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Could not restore syllabus backup {FileName}.",
                Path.GetFileName(backupPath));
        }
    }

    sealed class StagedSyllabusChange(
        string finalPath,
        string? backupPath,
        bool replacementInstalled,
        SemaphoreSlim mutationLock,
        ILogger logger) : IStagedSyllabusChange
    {
        bool completed;

        public ValueTask CommitAsync()
        {
            if (completed)
            {
                return ValueTask.CompletedTask;
            }

            completed = true;
            try
            {
                if (backupPath is null)
                {
                    return ValueTask.CompletedTask;
                }

                try
                {
                    File.Delete(backupPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        exception,
                        "The previous syllabus backup {FileName} could not be cleaned up.",
                        Path.GetFileName(backupPath));
                }
            }
            finally
            {
                mutationLock.Release();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (completed)
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                completed = true;
                if (replacementInstalled)
                {
                    File.Delete(finalPath);
                }

                if (backupPath is not null && File.Exists(backupPath))
                {
                    if (File.Exists(finalPath))
                    {
                        throw new IOException(
                            $"The syllabus rollback destination '{Path.GetFileName(finalPath)}' already exists.");
                    }

                    File.Move(backupPath, finalPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(
                    exception,
                    "Could not roll back a staged syllabus file change for {FileName}.",
                    Path.GetFileName(finalPath));
                throw new SyllabusStorageException(
                    "The syllabus file change could not be rolled back.",
                    exception);
            }
            finally
            {
                mutationLock.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

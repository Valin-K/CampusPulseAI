namespace CourseManagement.Backend.Services;

public sealed class SyllabusStorageOptions
{
    public const string SectionName = "SyllabusStorage";
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

    public string RootPath { get; set; } = "syllabus-files";
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
}

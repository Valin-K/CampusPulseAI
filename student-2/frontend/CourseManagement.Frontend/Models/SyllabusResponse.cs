namespace CourseManagement.Frontend.Models;

public sealed record SyllabusResponse(
    int CourseId,
    string FileName,
    string ContentType,
    long SizeBytes);

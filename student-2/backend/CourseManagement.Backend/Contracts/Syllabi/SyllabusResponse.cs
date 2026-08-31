namespace CourseManagement.Backend.Contracts.Syllabi;

public sealed record SyllabusResponse(
    int CourseId,
    string FileName,
    string ContentType,
    long SizeBytes);

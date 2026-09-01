using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AcademicRecords.Backend;

public interface ITranscriptPdfGenerator
{
    byte[] Generate(TranscriptData transcript);
}

public sealed class TranscriptPdfGenerator : ITranscriptPdfGenerator
{
    public byte[] Generate(TranscriptData transcript) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Content().Text(string.Empty);
        })).GeneratePdf();
}

public sealed record TranscriptData(string StudentId, string? FirstName, string? LastName);

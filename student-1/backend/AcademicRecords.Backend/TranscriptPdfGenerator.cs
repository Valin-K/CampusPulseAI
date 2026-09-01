using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace AcademicRecords.Backend;

public interface ITranscriptPdfGenerator
{
    byte[] Generate(TranscriptData transcript);
}

public sealed class TranscriptPdfGenerator : ITranscriptPdfGenerator
{
    static TranscriptPdfGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Generate(TranscriptData transcript) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontFamily("Arial").FontSize(8.5f).FontColor(Colors.Grey.Darken4));
            page.Header().Element(container => ComposeHeader(container, transcript));
            page.Content().Element(container => ComposeContent(container, transcript));
            page.Footer().Element(container => ComposeFooter(container, transcript));
        })).GeneratePdf();

    private static void ComposeHeader(IContainer container, TranscriptData transcript)
    {
        container.Column(column =>
        {
            column.Spacing(3);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Date printed: {transcript.PrintedAt:dd MMMM yyyy}");
                row.AutoItem().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
            column.Item().PaddingTop(5).AlignCenter().Text(transcript.InstitutionName).Bold().FontSize(13);
            column.Item().AlignCenter().Text(transcript.DocumentTitle).FontSize(10);
            column.Item().PaddingTop(8).Text(text =>
            {
                text.Span(transcript.StudentName).Bold().FontSize(11);
                text.Span("\nStudent Number: ");
                text.Span(transcript.StudentId);
            });
            column.Item().Text(text =>
            {
                if (!string.IsNullOrWhiteSpace(transcript.DegreeCode))
                {
                    text.Span(transcript.DegreeCode).Bold();
                    text.Span(" ");
                }

                text.Span(transcript.DegreeTitle ?? "Degree not supplied");
                if (!string.IsNullOrWhiteSpace(transcript.DegreeStatus))
                {
                    text.Span("\n(");
                    text.Span(transcript.DegreeStatus);
                    text.Span(")");
                }
            });
            if (!string.IsNullOrWhiteSpace(transcript.Major))
            {
                column.Item().Text($"Major in {transcript.Major}");
            }
        });
    }

    private static void ComposeContent(IContainer container, TranscriptData transcript)
    {
        container.Column(column =>
        {
            column.Spacing(7);
            column.Item().Element(content => ComposeSubjectSection(content, "Enrolled Subjects", transcript.EnrolledSubjects, false));
            column.Item().Element(content => ComposeSubjectSection(content, "Completed Subjects", transcript.CompletedSubjects, true));
            column.Item().Element(content => ComposeStatistics(content, transcript));
        });
    }

    private static void ComposeSubjectSection(
        IContainer container,
        string title,
        IReadOnlyList<TranscriptSubject> subjects,
        bool completed)
    {
        container.Column(column =>
        {
            column.Item().Text(title).Bold().FontSize(9.5f);
            if (subjects.Count == 0)
            {
                column.Item().PaddingTop(3).Text("None recorded.").Italic();
                return;
            }

            column.Item().Table(table =>
            {
                if (completed)
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(48);
                        columns.RelativeColumn(3.2f);
                        columns.ConstantColumn(36);
                        columns.ConstantColumn(35);
                        columns.RelativeColumn(1.6f);
                        columns.ConstantColumn(32);
                        columns.ConstantColumn(32);
                    });
                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Code");
                        HeaderCell(header.Cell(), "Subject");
                        HeaderCell(header.Cell(), "Year");
                        HeaderCell(header.Cell(), "Term");
                        HeaderCell(header.Cell(), "Result");
                        HeaderCell(header.Cell(), "Mark");
                        HeaderCell(header.Cell(), "Credit\nPoints\nGained");
                    });
                    foreach (var subject in subjects)
                    {
                        SubjectCell(table.Cell(), subject.CourseCode);
                        SubjectCell(table.Cell(), subject.CourseTitle);
                        SubjectCell(table.Cell(), subject.AcademicYear.ToString(CultureInfo.InvariantCulture));
                        SubjectCell(table.Cell(), subject.TermCode);
                        SubjectCell(table.Cell(), subject.Result);
                        SubjectCell(table.Cell(), FormatMark(subject.Mark), true);
                        SubjectCell(table.Cell(), FormatCredits(subject.CreditsGained), true);
                    }
                }
                else
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(48);
                        columns.RelativeColumn(3.2f);
                        columns.ConstantColumn(40);
                        columns.ConstantColumn(38);
                        columns.RelativeColumn(1.8f);
                        columns.ConstantColumn(43);
                    });
                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Code");
                        HeaderCell(header.Cell(), "Subject");
                        HeaderCell(header.Cell(), "Year");
                        HeaderCell(header.Cell(), "Term");
                        HeaderCell(header.Cell(), "Status");
                        HeaderCell(header.Cell(), "Credit\nPoints");
                    });
                    foreach (var subject in subjects)
                    {
                        SubjectCell(table.Cell(), subject.CourseCode);
                        SubjectCell(table.Cell(), subject.CourseTitle);
                        SubjectCell(table.Cell(), subject.AcademicYear.ToString(CultureInfo.InvariantCulture));
                        SubjectCell(table.Cell(), subject.TermCode);
                        SubjectCell(table.Cell(), subject.Status);
                        SubjectCell(table.Cell(), FormatCredits(subject.CreditsEnrolled), true);
                    }
                }
            });
        });
    }

    private static void ComposeStatistics(IContainer container, TranscriptData transcript)
    {
        container.PaddingTop(2).Column(column =>
        {
            column.Item().BorderTop(0.7f).BorderColor(Colors.Grey.Medium).PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text($"Total Credit Points Gained {FormatCredits(transcript.TotalCreditPointsGained)}");
                row.AutoItem().Text($"GPA {FormatStatistic(transcript.Gpa)}");
                row.AutoItem().PaddingLeft(18).Text($"WAM {FormatStatistic(transcript.Wam)}");
            });
        });
    }

    private static void ComposeFooter(IContainer container, TranscriptData transcript)
    {
        container.Border(0.8f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten2).Padding(6)
            .AlignCenter().Text(transcript.Disclaimer).FontSize(7.5f);
    }

    private static void HeaderCell(IContainer container, string value) =>
        container.Background(Colors.Grey.Lighten2).BorderBottom(0.7f).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(4).PaddingHorizontal(3).Text(value).Bold().FontSize(7.5f);

    private static void SubjectCell(IContainer container, string? value, bool alignRight = false)
    {
        var cell = container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3).PaddingHorizontal(3);
        if (alignRight)
        {
            cell.AlignRight().Text(value ?? string.Empty);
        }
        else
        {
            cell.Text(value ?? string.Empty);
        }
    }

    private static string FormatCredits(decimal? credits) =>
        credits?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatMark(decimal? mark) =>
        mark?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatStatistic(decimal? statistic) =>
        statistic?.ToString("0.00", CultureInfo.InvariantCulture) ?? "N/A";
}

public sealed record TranscriptData(
    string InstitutionName,
    string DocumentTitle,
    DateTime PrintedAt,
    string StudentId,
    string? FirstName,
    string? LastName,
    string? DegreeCode,
    string? DegreeTitle,
    string? DegreeStatus,
    string? Major,
    IReadOnlyList<TranscriptSubject> EnrolledSubjects,
    IReadOnlyList<TranscriptSubject> CompletedSubjects,
    decimal? Gpa,
    decimal? Wam,
    decimal TotalCreditPointsGained,
    string Disclaimer)
{
    public string StudentName => string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}


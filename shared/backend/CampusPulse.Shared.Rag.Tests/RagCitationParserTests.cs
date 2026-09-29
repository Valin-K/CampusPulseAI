using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;

namespace CampusPulse.Shared.Rag.Tests;

public sealed class RagCitationParserTests
{
    private readonly RagCitationParser _parser = new();

    private static RagSource Source(int rank, string documentId, string? title = null) =>
        new(rank, documentId, 0, 0.5, $"Text for {documentId}.",
            title is null ? null : new Dictionary<string, string> { ["title"] = title });

    private static IReadOnlyList<RagSource> Sources(int count) =>
        Enumerable.Range(1, count).Select(rank => Source(rank, $"doc-{rank}")).ToList();

    [Test]
    public void Parse_SingleMarker_BindsToTheMatchingSource()
    {
        var parsed = _parser.Parse("Hire lasts seven days [1].", Sources(2));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Cited, Has.Count.EqualTo(1));
            Assert.That(parsed.Cited[0].DisplayNumber, Is.EqualTo(1));
            Assert.That(parsed.Cited[0].Source.DocumentId, Is.EqualTo("doc-1"));
            Assert.That(CitationRenderer.ToText(parsed), Is.EqualTo("Hire lasts seven days [1]."));
        });
    }

    [Test]
    public void Parse_CommaList_ExpandsToEachSource()
    {
        var parsed = _parser.Parse("See [1,3].", Sources(3));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Cited.Select(cited => cited.Source.DocumentId), Is.EqualTo(new[] { "doc-1", "doc-3" }));
            Assert.That(CitationRenderer.ToText(parsed), Is.EqualTo("See [1, 2]."));
        });
    }

    [Test]
    public void Parse_Range_ExpandsInclusively()
    {
        var parsed = _parser.Parse("See [1-3].", Sources(3));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Cited, Has.Count.EqualTo(3));
            Assert.That(CitationRenderer.ToText(parsed), Is.EqualTo("See [1, 2, 3]."));
        });
    }

    [Test]
    public void Parse_Renumbers_InFirstUseOrder()
    {
        var parsed = _parser.Parse("First [2] then [1].", Sources(3));

        Assert.Multiple(() =>
        {
            // Source ranked 2 is used first, so it becomes display number 1.
            Assert.That(parsed.Cited[0].DisplayNumber, Is.EqualTo(1));
            Assert.That(parsed.Cited[0].Source.DocumentId, Is.EqualTo("doc-2"));
            Assert.That(parsed.Cited[1].DisplayNumber, Is.EqualTo(2));
            Assert.That(parsed.Cited[1].Source.DocumentId, Is.EqualTo("doc-1"));
            Assert.That(CitationRenderer.ToText(parsed), Is.EqualTo("First [1] then [2]."));
        });
    }

    [Test]
    public void Parse_OutOfRangeMarker_IsFlaggedAndDroppedFromText()
    {
        var parsed = _parser.Parse("A claim [9].", Sources(2));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.InvalidMarkers, Does.Contain(9));
            Assert.That(parsed.Cited, Is.Empty);
            Assert.That(CitationRenderer.ToText(parsed), Does.Not.Contain("[9]"));
        });
    }

    [Test]
    public void Parse_NonNumericBracket_IsLeftAsLiteralText()
    {
        const string answer = "See [see appendix] for detail.";
        var parsed = _parser.Parse(answer, Sources(1));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Cited, Is.Empty);
            Assert.That(parsed.InvalidMarkers, Is.Empty);
            Assert.That(CitationRenderer.ToText(parsed), Is.EqualTo(answer));
        });
    }

    [Test]
    public void Parse_DuplicateMarker_IsDedupedButRenderedEachTime()
    {
        var parsed = _parser.Parse("Fact [1] and again [1].", Sources(1));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Cited, Has.Count.EqualTo(1));
            Assert.That(CitationRenderer.ToText(parsed), Is.EqualTo("Fact [1] and again [1]."));
        });
    }

    [Test]
    public void Parse_NullAnswer_ReturnsEmptySegmentsAndAllSourcesUncited()
    {
        var sources = Sources(2);
        var parsed = _parser.Parse(null, sources);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Segments, Is.Empty);
            Assert.That(parsed.Cited, Is.Empty);
            Assert.That(parsed.Uncited, Is.EqualTo(sources));
            Assert.That(parsed.InvalidMarkers, Is.Empty);
        });
    }

    [Test]
    public void Parse_SeparatesCitedFromUncitedSources()
    {
        var parsed = _parser.Parse("Only the first [1].", Sources(3));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Cited.Select(cited => cited.Source.DocumentId), Is.EqualTo(new[] { "doc-1" }));
            Assert.That(parsed.Uncited.Select(source => source.DocumentId), Is.EqualTo(new[] { "doc-2", "doc-3" }));
        });
    }

    [Test]
    public void Parse_PreservesSurroundingTextAsSegments()
    {
        var parsed = _parser.Parse("Before [1] after.", Sources(1));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Segments[0], Is.InstanceOf<TextSegment>());
            Assert.That(((TextSegment)parsed.Segments[0]).Text, Is.EqualTo("Before "));
            Assert.That(parsed.Segments[1], Is.InstanceOf<CitationSegment>());
            Assert.That(((TextSegment)parsed.Segments[2]).Text, Is.EqualTo(" after."));
        });
    }

    [Test]
    public void Parse_ToleratesWhitespaceInsideMarkers()
    {
        var parsed = _parser.Parse("Spaced [ 1 ].", Sources(1));

        Assert.That(CitationRenderer.ToText(parsed), Is.EqualTo("Spaced [1]."));
    }

    [Test]
    public void Parse_WithNoSources_TreatsEveryMarkerAsInvalid()
    {
        var parsed = _parser.Parse("Unsupported [1].", []);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.InvalidMarkers, Does.Contain(1));
            Assert.That(parsed.Cited, Is.Empty);
        });
    }
}

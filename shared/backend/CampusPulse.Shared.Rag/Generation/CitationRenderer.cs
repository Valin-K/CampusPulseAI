using System.Text;

namespace CampusPulse.Shared.Rag.Generation;

public static class CitationRenderer
{
    public static string ToText(ParsedRagAnswer parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var builder = new StringBuilder();
        foreach (var segment in parsed.Segments)
        {
            switch (segment)
            {
                case TextSegment text:
                    builder.Append(text.Text);
                    break;
                case CitationSegment citation:
                    builder.Append('[').Append(string.Join(", ", citation.DisplayNumbers)).Append(']');
                    break;
            }
        }

        return builder.ToString();
    }
}

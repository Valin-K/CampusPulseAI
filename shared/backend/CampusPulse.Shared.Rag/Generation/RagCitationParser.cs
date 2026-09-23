using System.Text.RegularExpressions;
using CampusPulse.Shared.Rag.Contracts;

namespace CampusPulse.Shared.Rag.Generation;

public sealed partial class RagCitationParser
{
    [GeneratedRegex(@"\[\s*(\d+(?:\s*[-,]\s*\d+)*)\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();

    public ParsedRagAnswer Parse(string? answer, IReadOnlyList<RagSource> sources)
    {
        sources ??= [];

        if (string.IsNullOrEmpty(answer))
        {
            return new ParsedRagAnswer([], [], sources, []);
        }

        var segments = new List<AnswerSegment>();
        var invalidMarkers = new List<int>();
        var displayByRank = new Dictionary<int, int>();
        var citedInOrder = new List<CitedSource>();

        var lastIndex = 0;
        foreach (Match match in MarkerPattern().Matches(answer))
        {
            var ranks = ParseRanks(match.Groups[1].Value);
            var validRanks = new List<int>();
            foreach (var rank in ranks)
            {
                if (rank >= 1 && rank <= sources.Count)
                {
                    validRanks.Add(rank);
                }
                else
                {
                    invalidMarkers.Add(rank);
                }
            }

            if (match.Index > lastIndex)
            {
                segments.Add(new TextSegment(answer[lastIndex..match.Index]));
            }
            lastIndex = match.Index + match.Length;

            if (validRanks.Count == 0)
            {
                continue;
            }

            var displayNumbers = new List<int>();
            foreach (var rank in validRanks)
            {
                if (!displayByRank.TryGetValue(rank, out var display))
                {
                    display = displayByRank.Count + 1;
                    displayByRank[rank] = display;
                    citedInOrder.Add(new CitedSource(display, sources[rank - 1]));
                }
                displayNumbers.Add(display);
            }

            segments.Add(new CitationSegment(displayNumbers, [.. validRanks.Select(rank =>
                citedInOrder.First(cited => ReferenceEquals(cited.Source, sources[rank - 1])))]));
        }

        if (lastIndex < answer.Length)
        {
            segments.Add(new TextSegment(answer[lastIndex..]));
        }

        var citedRanks = displayByRank.Keys.ToHashSet();
        var uncited = sources
            .Where((_, index) => !citedRanks.Contains(index + 1))
            .ToList();

        return new ParsedRagAnswer(segments, citedInOrder, uncited, invalidMarkers);
    }

    private static IEnumerable<int> ParseRanks(string content)
    {
        foreach (var part in content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (bounds.Length == 1 && int.TryParse(bounds[0], out var single))
            {
                yield return single;
            }
            else if (bounds.Length == 2 && int.TryParse(bounds[0], out var from) && int.TryParse(bounds[1], out var to))
            {
                var low = Math.Min(from, to);
                var high = Math.Max(from, to);
                for (var rank = low; rank <= high; rank++)
                {
                    yield return rank;
                }
            }
        }
    }
}

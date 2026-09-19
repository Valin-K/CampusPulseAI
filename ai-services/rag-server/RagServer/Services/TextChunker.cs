using Microsoft.Extensions.Options;
using RagServer.Models;

namespace RagServer.Services;

public sealed class TextChunker(IOptions<RagOptions> options)
{
    public IReadOnlyList<string> Split(string text)
    {
        var size = Math.Max(100, options.Value.ChunkSize);
        var overlap = Math.Clamp(options.Value.ChunkOverlap, 0, size / 2);
        var normalised = text.Trim();
        if (normalised.Length <= size)
        {
            return normalised.Length == 0 ? [] : [normalised];
        }

        var chunks = new List<string>();
        var step = size - overlap;
        for (var start = 0; start < normalised.Length; start += step)
        {
            var length = Math.Min(size, normalised.Length - start);
            var chunk = normalised.Substring(start, length).Trim();
            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            if (start + length >= normalised.Length)
            {
                break;
            }
        }

        return chunks;
    }
}

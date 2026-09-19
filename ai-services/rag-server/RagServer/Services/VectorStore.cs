using System.Collections.Concurrent;
using RagServer.Models;

namespace RagServer.Services;

public sealed class VectorStore
{
    private readonly ConcurrentDictionary<string, List<StoredChunk>> _collections = new(StringComparer.OrdinalIgnoreCase);

	// update or insert chunks for a given document in a collection
	public void Upsert(string collection, string documentId, IEnumerable<StoredChunk> chunks)
    {
        var entries = _collections.GetOrAdd(collection, _ => []);
        var normalised = chunks.Select(chunk => chunk with { Vector = Normalise(chunk.Vector) }).ToList();
        lock (entries)
        {
            entries.RemoveAll(entry => string.Equals(entry.DocumentId, documentId, StringComparison.Ordinal));
            entries.AddRange(normalised);
        }
    }

	public IReadOnlyList<(StoredChunk Chunk, double Score)> Search(string collection, float[] query, int topK)
    {
        if (!_collections.TryGetValue(collection, out var entries))
        {
            return [];
        }

        var normalisedQuery = Normalise(query);
        StoredChunk[] snapshot;
        lock (entries)
        {
            snapshot = [.. entries];
        }

        return snapshot
            .Where(entry => entry.Vector.Length == normalisedQuery.Length)
            .Select(entry => (Chunk: entry, Score: Dot(entry.Vector, normalisedQuery)))
            .OrderByDescending(hit => hit.Score)
            .Take(topK)
            .ToList();
    }

    public bool Delete(string collection) => _collections.TryRemove(collection, out _);

	// for diagnostic purposes, return a summary of all collections, including the number of unique documents and total chunks in each collection
	public IReadOnlyList<CollectionSummary> Summaries()
    {
        return _collections
            .Select(pair =>
            {
                lock (pair.Value)
                {
                    return new CollectionSummary(
                        pair.Key,
                        pair.Value.Select(entry => entry.DocumentId).Distinct(StringComparer.Ordinal).Count(),
                        pair.Value.Count);
                }
            })
            .OrderBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static float[] Normalise(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(value => (double)value * value));
        if (magnitude == 0)
        {
            return vector;
        }

        var result = new float[vector.Length];
        for (var index = 0; index < vector.Length; index++)
        {
            result[index] = (float)(vector[index] / magnitude);
        }

        return result;
    }

    private static double Dot(float[] left, float[] right)
    {
        double total = 0;
        for (var index = 0; index < left.Length; index++)
        {
            total += (double)left[index] * right[index];
        }

        return Math.Clamp(total, -1, 1);
    }
}

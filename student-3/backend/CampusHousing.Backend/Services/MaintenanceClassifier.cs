using System.Net.Http.Json;
using System.Text.Json;

namespace CampusHousing.Backend.Services;

public sealed record MaintenanceClassification(string Severity, string RepairQueue);

public sealed class MaintenanceClassifier(IHttpClientFactory clients, IConfiguration configuration, ILogger<MaintenanceClassifier> logger)
{
    public async Task<MaintenanceClassification> ClassifyAsync(string description, CancellationToken cancellationToken)
    {
        var fallback = Fallback(description);
        var apiKey = configuration["GEMINI_API_KEY"];
        var model = configuration["Gemini:Model"] ?? "gemini-3.5-flash-lite";
        if (string.IsNullOrWhiteSpace(apiKey)) return fallback;

        try
        {
            var prompt = $"Classify this campus maintenance request. Return only JSON with Severity (Emergency, High, Medium, or Low) and RepairQueue (Heating, Plumbing, Electrical, Security, or General Maintenance). Request: {description}";
            var request = new GeminiGenerateRequest([new GeminiContent([new GeminiPart(prompt)])]);
            using var response = await clients.CreateClient("gemini")
                .PostAsJsonAsync($"v1beta/models/{model}:generateContent", request, cancellationToken);
            if (!response.IsSuccessStatusCode) return fallback;
            var result = await response.Content.ReadFromJsonAsync<GeminiGenerateResponse>(cancellationToken: cancellationToken);
            var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var json = text.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
                json = firstLineEnd >= 0 && lastFence > firstLineEnd ? json[(firstLineEnd + 1)..lastFence].Trim() : json;
            }
            var parsed = JsonSerializer.Deserialize<MaintenanceClassification>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed is null ? fallback : Normalize(parsed);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Gemini was unavailable; using rule-based maintenance classification.");
            return fallback;
        }
    }

    private static MaintenanceClassification Fallback(string description)
    {
        var text = description.ToLowerInvariant();
        var severity = text.Contains("fire") || text.Contains("gas") || text.Contains("flood") || text.Contains("unsafe") ? "Emergency" :
            text.Contains("no heat") || text.Contains("broken heater") || text.Contains("leak") ? "High" : "Low";
        var queue = text.Contains("heat") || text.Contains("heater") ? "Heating" : text.Contains("leak") || text.Contains("water") ? "Plumbing" : text.Contains("light") || text.Contains("power") ? "Electrical" : text.Contains("lock") || text.Contains("security") ? "Security" : "General Maintenance";
        return new MaintenanceClassification(severity, queue);
    }

    private static MaintenanceClassification Normalize(MaintenanceClassification value)
    {
        var severity = value.Severity?.Trim();
        var queue = value.RepairQueue?.Trim();
        return new(new[] { "Emergency", "High", "Medium", "Low" }.Contains(severity, StringComparer.OrdinalIgnoreCase) ? severity! : "Low",
            new[] { "Heating", "Plumbing", "Electrical", "Security", "General Maintenance" }.Contains(queue, StringComparer.OrdinalIgnoreCase) ? queue! : "General Maintenance");
    }

    private sealed record GeminiGenerateRequest(IReadOnlyList<GeminiContent> Contents);
    private sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiPart(string Text);
    private sealed record GeminiGenerateResponse(IReadOnlyList<GeminiCandidate>? Candidates);
    private sealed record GeminiCandidate(GeminiContent? Content);
}

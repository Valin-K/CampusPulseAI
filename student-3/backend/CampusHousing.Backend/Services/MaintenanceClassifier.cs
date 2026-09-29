using CampusPulse.Shared.Ai;
using System.Text.Json;

namespace CampusHousing.Backend.Services;

public sealed record MaintenanceClassification(string Severity, string RepairQueue);

public sealed class MaintenanceClassifier(IAiGatewayClient gateway, ILogger<MaintenanceClassifier> logger)
{
    public async Task<MaintenanceClassification> ClassifyAsync(string description, CancellationToken cancellationToken)
    {
        var fallback = Fallback(description);
        try
        {
            const string prompt = "Classify this campus maintenance request. Return only JSON with Severity (Emergency, High, Medium, or Low) and RepairQueue (Heating, Plumbing, Electrical, Security, or General Maintenance).";
            var result = await gateway.GenerateAsync(prompt, $"Request: {description}", cancellationToken);
            var json = result.Text.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
                json = firstLineEnd >= 0 && lastFence > firstLineEnd ? json[(firstLineEnd + 1)..lastFence].Trim() : json;
            }
            var parsed = JsonSerializer.Deserialize<MaintenanceClassification>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null)
            {
                logger.LogWarning("The AI service returned an empty maintenance classification; using rules.");
                return fallback;
            }
            return Normalize(parsed);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The AI gateway timed out; using rule-based maintenance classification.");
            return fallback;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(exception, "The AI gateway was unavailable; using rule-based maintenance classification.");
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

}

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public sealed class GeminiScholarshipService(IHttpClientFactory clients, IConfiguration configuration)
{
    public async Task<ScholarshipRecommendationResponse> RecommendAsync(StudentProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"]))
        {
            throw new InvalidOperationException("GEMINI_API_KEY is not configured.");
        }

        var scholarships = await clients.CreateClient("billingDatabase")
            .GetFromJsonAsync<List<ScholarshipResponse>>("/api/scholarships", cancellationToken) ?? [];
        var matches = scholarships
            .Where(scholarship => scholarship.IsActive)
            .Where(scholarship => scholarship.MinimumGpa <= profile.Gpa)
            .Where(scholarship => scholarship.MinimumYearLevel <= profile.YearLevel)
            .Where(scholarship => MajorMatches(profile.Major, scholarship.EligibleMajor))
            .OrderByDescending(scholarship => scholarship.AwardAmount)
            .ToList();

        var context = new StringBuilder();
        foreach (var scholarship in matches)
        {
            context.AppendLine(CultureInfo.InvariantCulture, $"- {scholarship.Name}: {scholarship.Description} Award ${scholarship.AwardAmount:N2}; minimum GPA {scholarship.MinimumGpa:F2}; eligible major {scholarship.EligibleMajor}; minimum year {scholarship.MinimumYearLevel}; deadline {scholarship.ApplicationDeadline:yyyy-MM-dd}.");
        }

        var prompt = $"""
            You are a university financial aid assistant. Recommend scholarships only from the supplied database context.

            Return plain text, not Markdown or HTML. Do not use asterisks, headings, or a greeting and sign-off. List every supplied match in descending award order using exactly this readable layout, with one blank line between scholarships:

            1. Scholarship name
            Why it fits: One concise sentence based only on explicit profile and scholarship data.
            Award: $0,000.00
            Deadline: Month d, yyyy

            Preserve each supplied award amount and deadline exactly. Do not invent eligibility rules, demographic attributes, or facts that are not explicitly present in the student profile.

            Student profile:
            Student ID: {profile.StudentId}
            Name: {profile.Name}
            Major: {profile.Major}
            GPA: {profile.Gpa:F2}
            Year level: {profile.YearLevel}
            First-generation: {profile.IsFirstGeneration}
            Interests: {string.Join(", ", profile.Interests)}

            Scholarship matches ({matches.Count}):
            {context}
            """;
        var request = new GeminiGenerateRequest([new GeminiContent([new GeminiPart(prompt)])]);
        var model = configuration["Gemini:Model"] ?? "gemini-3.5-flash-lite";
        using var response = await clients.CreateClient("gemini")
            .PostAsJsonAsync($"v1beta/models/{model}:generateContent", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiGenerateResponse>(cancellationToken: cancellationToken);
        var recommendation = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(recommendation))
        {
            throw new InvalidOperationException("Gemini returned an empty recommendation.");
        }

        return new(profile.StudentId, matches.Count, recommendation);
    }

    private static bool MajorMatches(string studentMajor, string eligibleMajor) =>
        eligibleMajor.Equals("Any", StringComparison.OrdinalIgnoreCase)
        || eligibleMajor.Equals(studentMajor, StringComparison.OrdinalIgnoreCase)
        || eligibleMajor.Equals("STEM", StringComparison.OrdinalIgnoreCase)
            && (studentMajor.Contains("Engineering", StringComparison.OrdinalIgnoreCase)
                || studentMajor.Contains("Science", StringComparison.OrdinalIgnoreCase)
                || studentMajor.Contains("Technology", StringComparison.OrdinalIgnoreCase)
                || studentMajor.Contains("Mathematics", StringComparison.OrdinalIgnoreCase));
}
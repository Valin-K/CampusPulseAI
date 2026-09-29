using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using CampusPulse.Shared.Ai;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public sealed class GeminiScholarshipService(IHttpClientFactory clients, IAiGatewayClient gateway)
{
    public async Task<ScholarshipRecommendationResponse> RecommendAsync(StudentProfile profile, CancellationToken cancellationToken)
    {
        var scholarships = await clients.CreateClient("billingDatabase")
            .GetFromJsonAsync<List<ScholarshipResponse>>("/api/scholarships", cancellationToken) ?? [];
        var matches = scholarships
            .Where(scholarship => scholarship.IsActive)
            .Where(scholarship => scholarship.MinimumYearLevel <= profile.YearLevel)
            .Where(scholarship => MajorMatches(profile.Major, scholarship.EligibleMajor))
            .OrderByDescending(scholarship => scholarship.AwardAmount)
            .ToList();

        var context = new StringBuilder();
        foreach (var scholarship in matches)
        {
            context.AppendLine(CultureInfo.InvariantCulture, $"- {scholarship.Name}: {scholarship.Description} Award ${scholarship.AwardAmount:N2}; minimum GPA {scholarship.MinimumGpa:F2}; eligible major {scholarship.EligibleMajor}; minimum year {scholarship.MinimumYearLevel}; deadline {scholarship.ApplicationDeadline:yyyy-MM-dd}.");
        }

        const string prompt = """
            You are a university financial aid assistant. Recommend scholarships only from the supplied database context.

            Return plain text, not Markdown or HTML. Do not use asterisks, headings, or a greeting and sign-off. List every supplied match in descending award order using exactly this readable layout, with one blank line between scholarships:

            1. Scholarship name
            Why it fits: One concise sentence based only on explicit profile and scholarship data.
            Award: $0,000.00
            Deadline: Month d, yyyy

            Preserve each supplied award amount and deadline exactly. Do not invent eligibility rules, demographic attributes, or facts that are not explicitly present in the student profile.
            """;

        var studentContext = $"""
            Student profile:
            Student ID: {profile.StudentId}
            Name: {profile.Name}
            Major: {profile.Major}
            Year level: {profile.YearLevel}

            Scholarship matches ({matches.Count}):
            {context}
            """;
        var result = await gateway.GenerateAsync(prompt, studentContext, cancellationToken);
        return new(profile.StudentId, matches.Count, result.Text);
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
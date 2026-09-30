using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using CampusPulse.Shared.Ai;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public sealed class GeminiScholarshipService(
    IHttpClientFactory clients, IAiGatewayClient gateway, ScholarshipAcademicService academic)
{
    public async Task<ScholarshipRecommendationResponse> RecommendAsync(StudentProfile profile, CancellationToken cancellationToken)
    {
        var academicContext = await academic.GetAsync(profile.AcademicStudentId, cancellationToken);
        var scholarships = await clients.CreateClient("billingDatabase")
            .GetFromJsonAsync<List<ScholarshipResponse>>("/api/scholarships", cancellationToken)
            ?? throw new InvalidOperationException("The scholarship database returned an empty response.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var matches = scholarships
            .Where(scholarship => scholarship.IsActive)
            .Where(scholarship => scholarship.ApplicationDeadline >= today)
            .Where(scholarship => scholarship.MinimumYearLevel <= profile.YearLevel)
            .Where(scholarship => MajorMatches(profile.Major, scholarship.EligibleMajor))
            .OrderByDescending(scholarship => scholarship.AwardAmount)
            .ToList();

        if (matches.Count == 0)
        {
            return new(profile.StudentId, 0,
                "No open, unexpired scholarships match your major and year level. GPA eligibility remains unverified.",
                academicContext);
        }

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
            These are potential matches by major and year, not confirmed eligibility. Academic Records GPA is on a 7-point scale, whereas scholarship minimum GPA is on a 4-point scale. Never compare or convert these scales, and never claim GPA eligibility or ineligibility.
            Include each scholarship's minimum GPA (4-point scale) and explain that aid-office review is required. If the MCP GPA is unavailable, say so; do not invent one.
            """;

        var studentContext = $"""
            Student profile:
            Student ID: {profile.StudentId}
            Name: {profile.Name}
            Major: {profile.Major}
            Year level: {profile.YearLevel}

            Academic statistics retrieved through MCP ({academicContext.Tool}):
            GPA (7-point scale): {academicContext.Gpa?.ToString("F2", CultureInfo.InvariantCulture) ?? "unavailable"}
            GPA eligibility: {academicContext.GpaEligibility}

            Scholarship matches ({matches.Count}):
            {context}
            """;
        var result = await gateway.GenerateAsync(prompt, studentContext, cancellationToken);
        return new(profile.StudentId, matches.Count, result.Text, academicContext);
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
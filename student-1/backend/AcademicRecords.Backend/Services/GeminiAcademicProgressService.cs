using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicRecords.Backend.Contracts;
using AcademicRecords.Backend.Models;

namespace AcademicRecords.Backend.Services;

public sealed class GeminiAcademicProgressService(IHttpClientFactory clients, IConfiguration configuration)
{
    public async Task<AcademicProgressAnalysisResponse> AnalyzeAsync(
        StudentStatisticsProfile profile,
        CancellationToken cancellationToken)
    {
        var metrics = CalculateMetrics(profile);
        var apiKey = configuration["GEMINI_API_KEY"] ?? configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Failed(metrics, "GEMINI_API_KEY is not configured.");
        }

        var prompt = CreatePrompt(profile, metrics);
        var request = new GeminiGenerateRequest([new GeminiContent([new GeminiPart(prompt)])]);
        var model = configuration["Gemini:Model"] ?? "gemini-3.5-flash-lite";

        try
        {
            using var response = await clients.CreateClient("gemini")
                .PostAsJsonAsync($"v1beta/models/{model}:generateContent", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GeminiGenerateResponse>(cancellationToken: cancellationToken);
            var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return Failed(metrics, "Gemini returned an empty analysis.");
            }

            var analysis = JsonSerializer.Deserialize<GeminiAnalysis>(RemoveMarkdownCodeFence(text),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNameCaseInsensitive = true
                });
            if (analysis is null || string.IsNullOrWhiteSpace(analysis.ExpectedGraduationTerm))
            {
                return Failed(metrics, "Gemini returned an invalid analysis.");
            }

            return new(
                true,
                "Succeeded",
                "Academic progress analysis completed.",
                new(analysis.ExpectedGraduationTerm, analysis.ExpectedGraduationDate ?? "Not available", analysis.Explanation ?? ""),
                analysis.RiskFlags?.Select(flag => new AcademicRiskFlag(
                    flag.Severity ?? "Info", flag.Category ?? "General", flag.Description ?? "", flag.RecommendedAction ?? "")).ToList() ?? [],
                metrics);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(metrics, "The AI request timed out.");
        }
        catch (HttpRequestException)
        {
            return Failed(metrics, "The AI service is unavailable.");
        }
        catch (JsonException)
        {
            return Failed(metrics, "The AI service returned an unreadable response.");
        }
    }

    private static AcademicProgressMetrics CalculateMetrics(StudentStatisticsProfile profile)
    {
        var enrollments = (profile.Enrollments ?? [])
            .Where(enrollment => enrollment.IsFinalized && enrollment.Credits is > 0)
            .ToList();
        var passed = enrollments.Where(IsPassed).ToList();
        var earnedCredits = passed.Sum(enrollment => enrollment.Credits!.Value);
        var years = enrollments.GroupBy(enrollment => enrollment.AcademicYear)
            .Where(group => group.Any()).ToList();
        var recent = years.OrderByDescending(group => group.Key)
            .ThenByDescending(group => group.Key).Take(2).SelectMany(group => group).ToList();
        var overall = CalculateGpa(enrollments);
        var recentGpa = CalculateGpa(recent);

        return new(
            earnedCredits,
            GraduationEligibilityCalculator.RequiredCreditPoints,
            Math.Max(0, GraduationEligibilityCalculator.RequiredCreditPoints - earnedCredits),
            years.Count == 0 ? 0 : earnedCredits / years.Count,
            overall,
            recentGpa,
            years.Count,
            enrollments.Count(enrollment => string.Equals(enrollment.Status, "Failed", StringComparison.OrdinalIgnoreCase)));
    }

    private static string CreatePrompt(StudentStatisticsProfile profile, AcademicProgressMetrics metrics) => $"""
        You are an academic progress assistant. Analyze only the supplied student record.
        Predict when the student is likely to complete a {metrics.RequiredCredits:0.##}-credit degree using their observed yearly pace.
        Flag declining GPA, repeated failures, and insufficient completion pace. Do not invent course requirements.
        Return JSON only with the fields expectedGraduationTerm, expectedGraduationDate, explanation, and riskFlags.
        Each riskFlags item must have severity, category, description, and recommendedAction.
        Use an empty riskFlags array when no risk is supported by the record.

        Student: {profile.FirstName} {profile.LastName}; degree: {profile.Degree}; major: {profile.Major}
        Earned credits: {metrics.EarnedCredits:0.##}; required credits: {metrics.RequiredCredits:0.##};
        credits remaining: {metrics.CreditsRemaining:0.##}; average credits per academic year: {metrics.AverageCreditsPerYear:0.##};
        overall GPA: {Format(metrics.OverallGpa)}; recent GPA: {Format(metrics.RecentGpa)};
        completed academic years: {metrics.CompletedYears}; failed courses: {metrics.FailedCourses}
        Enrollment history:
        {string.Join("\n", (profile.Enrollments ?? []).Select(enrollment => $"- {enrollment.AcademicYear} {enrollment.TermCode ?? enrollment.TermName}: {enrollment.CourseCode}, {enrollment.Credits} credits, {enrollment.Status}, grade {enrollment.GradeValue?.ToString("0.##") ?? "not graded"}"))}
        """;

    private static decimal? CalculateGpa(IEnumerable<StudentStatisticsEnrollment> enrollments)
    {
        var graded = enrollments.Where(enrollment => enrollment.GradeValue is not null && enrollment.Credits is > 0).ToList();
        var credits = graded.Sum(enrollment => enrollment.Credits!.Value);
        return credits == 0 ? null : graded.Sum(enrollment => StudentStatisticsCalculator.GradePoints(enrollment.GradeValue!.Value) * enrollment.Credits!.Value) / credits;
    }

    private static bool IsPassed(StudentStatisticsEnrollment enrollment) =>
        string.Equals(enrollment.Status, "Passed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(enrollment.Status, "Completed", StringComparison.OrdinalIgnoreCase);

    private static string Format(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Not available";

    private static AcademicProgressAnalysisResponse Failed(AcademicProgressMetrics metrics, string message) =>
        new(false, "Failed", message, null, [], metrics);

    private static string RemoveMarkdownCodeFence(string text)
    {
        var value = text.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = value.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                value = value[(firstLineEnd + 1)..];
            }

            var closingFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                value = value[..closingFence];
            }
        }

        return value;
    }

    private sealed record GeminiAnalysis(
        string? ExpectedGraduationTerm,
        string? ExpectedGraduationDate,
        string? Explanation,
        List<GeminiRiskFlag>? RiskFlags);

    private sealed record GeminiRiskFlag(string? Severity, string? Category, string? Description, string? RecommendedAction);
}

namespace AcademicRecords.Backend.Models;

public sealed record AcademicProgressAnalysisResponse(
    bool Succeeded,
    string Status,
    string? Message,
    AcademicProgressPrediction? Prediction,
    IReadOnlyList<AcademicRiskFlag> RiskFlags,
    AcademicProgressMetrics Metrics);

public sealed record AcademicProgressPrediction(
    string ExpectedGraduationTerm,
    string ExpectedGraduationDate,
    string Explanation);

public sealed record AcademicRiskFlag(
    string Severity,
    string Category,
    string Description,
    string RecommendedAction);

public sealed record AcademicProgressMetrics(
    decimal EarnedCredits,
    decimal RequiredCredits,
    decimal CreditsRemaining,
    decimal AverageCreditsPerYear,
    decimal? OverallGpa,
    decimal? RecentGpa,
    int CompletedYears,
    int FailedCourses);

public sealed record GeminiGenerateRequest(IReadOnlyList<GeminiContent> Contents);

public sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);

public sealed record GeminiPart(string Text);

public sealed record GeminiGenerateResponse(IReadOnlyList<GeminiCandidate>? Candidates);

public sealed record GeminiCandidate(GeminiContent? Content);

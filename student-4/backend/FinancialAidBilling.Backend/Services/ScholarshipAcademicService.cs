using System.Text.Json;
using System.Text.Json.Serialization;
using CampusPulse.Shared.Mcp.Client;
using FinancialAidBilling.Backend.Models;

namespace FinancialAidBilling.Backend.Services;

public sealed class ScholarshipAcademicService(IMcpClient mcp)
{
    public const string StatisticsTool = "calculate_student_academic_statistics";

    public async Task<ScholarshipAcademicContext> GetAsync(string studentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new InvalidOperationException("Academic Records did not provide a student identifier.");
        }

        var response = await mcp.CallAsync(StatisticsTool,
            new Dictionary<string, JsonElement> { ["student_id"] = JsonSerializer.SerializeToElement(studentId) },
            cancellationToken);
        if (response.IsError || response.Tool != StatisticsTool || response.ExecutedAtUtc == default)
        {
            throw new McpResponseException("The MCP academic statistics tool did not succeed.");
        }

        AcademicStatistics? statistics;
        try
        {
            statistics = response.Result.Deserialize<AcademicStatistics>(JsonSerializerOptions.Web);
        }
        catch (JsonException exception)
        {
            throw new McpResponseException("MCP returned malformed academic statistics.", exception);
        }

        if (statistics is null || statistics.StudentId != studentId
            || statistics.Gpa is null || statistics.Gpa.StudentId != studentId
            || statistics.Gpa.Statistic != "GPA" || statistics.Gpa.Value is < 0 or > 7)
        {
            throw new McpResponseException("MCP returned invalid academic statistics for this student.");
        }

        return new(studentId, statistics.Gpa.Value, 7,
            statistics.Gpa.Value is null
                ? "Unverified: no finalized GPA is available. Scholarship GPA requirements still need aid-office review."
                : "Unverified: Academic Records uses a 7-point GPA and scholarships use a 4-point GPA. Aid-office review is required; no conversion has been applied.",
            response.Tool, response.ExecutedAtUtc);
    }

    private sealed record AcademicStatistics(
        [property: JsonRequired] string StudentId,
        [property: JsonRequired] GpaStatistic Gpa);

    private sealed record GpaStatistic(
        [property: JsonRequired] string StudentId,
        [property: JsonRequired] string Statistic,
        [property: JsonRequired] decimal? Value);
}

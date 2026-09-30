using McpServer.Contracts;

namespace McpServer.Tools.Features;

public sealed class GetFeatureCapabilitiesTool() : McpTool(
    "get_feature_capabilities",
    "Return the supported MCP capability for a student feature.",
    "Features",
    new Dictionary<string, string>
    {
        ["feature"] = "student-1 through student-5"
    })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var feature = McpToolArguments.GetString(request.Arguments, "feature");
        if (!McpToolArguments.IsSupportedFeature(feature))
        {
            throw new McpToolArgumentException(
                "A supported feature argument is required.");
        }

        var registeredTools = request.RegisteredTools
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();
        var featureTools = registeredTools
            .Where(tool => FeatureToolOwners.Matches(feature, tool.Owner))
            .Select(tool => tool.Name)
            .ToArray();

        return Task.FromResult(new McpToolExecution(false, new
        {
            feature,
            aiMode = true,
            mcp = featureTools.Length > 0,
            featureTools,
            registeredTools = registeredTools.Select(tool => tool.Name).ToArray()
        }));
    }
}

public sealed class ValidateIntegrationTool() : McpTool(
    "validate_integration",
    "Validate the complete shared MCP tool registry for a supported feature.",
    "Features",
    new Dictionary<string, string>
    {
        ["feature"] = "student-1 through student-5"
    })
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedToolOwners =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["student_count"] = "Academic Records",
            ["students_by_subject"] = "Academic Records",
            ["project_files"] = "Repository",
            ["ci_report"] = "Repository",
            ["get_feature_capabilities"] = "Features",
            ["validate_integration"] = "Features",
            ["calculate_student_academic_statistics"] = "Academic Records",
            ["get_student_enrollments"] = "Academic Records",
            ["course.list_courses"] = "Course Management",
            ["course.get_details"] = "Course Management",
            ["course.get_prerequisites"] = "Course Management",
            ["course.get_faculty_assignments"] = "Course Management",
            ["course.check_prerequisite_eligibility"] = "Course Management"
        };

    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var feature = McpToolArguments.GetString(request.Arguments, "feature");
        if (!McpToolArguments.IsSupportedFeature(feature))
        {
            throw new McpToolArgumentException(
                "A supported feature argument is required.");
        }

        var registeredTools = request.RegisteredTools
            .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var registeredNames = registeredTools
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingTools = ExpectedToolOwners.Keys
            .Where(name => !registeredNames.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpectedTools = registeredNames
            .Where(name => !ExpectedToolOwners.ContainsKey(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateTools = registeredTools
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expectedGlobalFeatureTools = ExpectedToolOwners
            .Where(pair => string.Equals(pair.Value, "Features", StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingGlobalFeatureTools = expectedGlobalFeatureTools
            .Where(name => !registeredNames.Contains(name))
            .ToArray();
        var ownerMismatches = registeredTools
            .Where(tool => ExpectedToolOwners.TryGetValue(tool.Name, out var expectedOwner)
                && !string.Equals(tool.Owner, expectedOwner, StringComparison.Ordinal))
            .Select(tool => new
            {
                name = tool.Name,
                expectedOwner = ExpectedToolOwners[tool.Name],
                actualOwner = tool.Owner
            })
            .ToArray();
        var toolContractsComplete = registeredTools.All(tool =>
            !string.IsNullOrWhiteSpace(tool.Name)
            && !string.IsNullOrWhiteSpace(tool.Description)
            && !string.IsNullOrWhiteSpace(tool.Owner)
            && tool.InputSchema is not null
            && tool.InputSchema.All(argument =>
                !string.IsNullOrWhiteSpace(argument.Key)
                && !string.IsNullOrWhiteSpace(argument.Value)));
        var checks = new[]
        {
            new { name = "feature-allowlisted", passed = true },
            new { name = "all-expected-tools-registered", passed = missingTools.Length == 0 },
            new { name = "no-unexpected-tools", passed = unexpectedTools.Length == 0 },
            new { name = "global-feature-tools-registered", passed = missingGlobalFeatureTools.Length == 0 },
            new { name = "tool-owners-match", passed = ownerMismatches.Length == 0 },
            new { name = "tool-contracts-complete", passed = toolContractsComplete },
            new { name = "tool-names-unique", passed = duplicateTools.Length == 0 }
        };

        return Task.FromResult(new McpToolExecution(false, new
        {
            feature,
            valid = checks.All(check => check.passed),
            checks,
            registeredTools = registeredTools.Select(tool => tool.Name).ToArray(),
            expectedTools = ExpectedToolOwners.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            missingTools,
            unexpectedTools,
            missingGlobalFeatureTools,
            duplicateTools,
            ownerMismatches
        }));
    }
}

internal static class FeatureToolOwners
{
    private static readonly IReadOnlyDictionary<string, string> Owners =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["student-1"] = "Academic Records",
            ["student-2"] = "Course Management",
            ["student-3"] = "Campus Housing",
            ["student-4"] = "Financial Aid and Billing",
            ["student-5"] = "Event Scheduling"
        };

    public static bool Matches(string feature, string owner) =>
        Owners.TryGetValue(feature, out var expectedOwner)
        && string.Equals(owner, expectedOwner, StringComparison.Ordinal);
}

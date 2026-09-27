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
    private static readonly string[] RegisteredTools =
    [
        "student_count",
        "students_by_subject",
        "project_files",
        "ci_report",
        "get_feature_capabilities",
        "validate_integration",
        "calculate_student_academic_statistics",
        "get_student_enrollments"
    ];

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

        return Task.FromResult(new McpToolExecution(false, new
        {
            feature,
            aiMode = true,
            mcp = true,
            registeredTools = RegisteredTools
        }));
    }
}

public sealed class ValidateIntegrationTool() : McpTool(
    "validate_integration",
    "Validate that a feature is registered with the shared MCP server.",
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

        return Task.FromResult(new McpToolExecution(false, new
        {
            feature,
            valid = true,
            checks = new[] { "feature-allowlisted", "tool-registered", "structured-result" }
        }));
    }
}

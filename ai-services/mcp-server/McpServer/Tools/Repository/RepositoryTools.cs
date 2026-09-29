using McpServer.Contracts;

namespace McpServer.Tools.Repository;

public sealed class CiReportTool() : McpTool(
    "ci_report",
    "Read the latest local agentic-loop evidence report.",
    "Repository",
    new Dictionary<string, string>())
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var root = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName
            ?? Directory.GetCurrentDirectory();
        var evidenceDirectory = Path.Combine(root, "docs", "release-0", "agentic-loop-evidence");
        var latest = Directory.Exists(evidenceDirectory)
            ? Directory.EnumerateFiles(evidenceDirectory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        var result = latest is null
            ? new Dictionary<string, object?>
            {
                ["error"] = "No agentic-loop evidence report is available."
            }
            : new Dictionary<string, object?>
            {
                ["path"] = Path.GetRelativePath(root, latest),
                ["report"] = File.ReadAllText(latest)
            };

        return Task.FromResult(new McpToolExecution(false, result));
    }
}

public sealed class ProjectFilesTool() : McpTool(
    "project_files",
    "List files in the CampusPulseAI repository root or a safe child directory.",
    "Repository",
    new Dictionary<string, string>
    {
            ["directory_path"] = "repository-relative directory, default ."
        })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var relativePath = McpToolArguments.GetString(request.Arguments, "directory_path") ?? ".";
        var root = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName
            ?? Directory.GetCurrentDirectory();
        var requested = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!requested.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(requested))
        {
            return Task.FromResult(new McpToolExecution(false, new Dictionary<string, object?>
            {
                ["error"] = "Directory is outside the repository or does not exist."
            }));
        }

        return Task.FromResult(new McpToolExecution(false, new Dictionary<string, object?>
        {
            ["path"] = Path.GetRelativePath(root, requested),
            ["files"] = Directory.EnumerateFileSystemEntries(requested).Select(Path.GetFileName).Order().Take(100).ToArray()
        }));
    }
}

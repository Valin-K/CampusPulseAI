using McpServer.Contracts;
using McpServer.Tools;

namespace McpServer.Tools.CampusHousing;

public sealed class SearchRelatedTicketsTool(CampusHousingApiClient campusHousing)
    : McpTool(
        "housing.search_related_tickets",
        "Find currently open Campus Housing maintenance tickets related to a new incident description.",
        "Campus Housing",
        new Dictionary<string, string>
        {
            ["description"] = "The new maintenance incident description to compare with open tickets."
        })
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        var description = McpToolArguments.GetString(request.Arguments, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new McpToolArgumentException("A non-empty description argument is required.");
        }

        return campusHousing.SearchRelatedTicketsAsync(description, cancellationToken);
    }
}

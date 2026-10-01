using McpServer.Contracts;
using McpServer.Tools;

namespace McpServer.Tools.CampusHousing;

public sealed class RoomStatusTool(CampusHousingApiClient campusHousing)
    : McpTool(
        "room_status",
        "List Campus Housing rooms that currently have open beds and are available for allocation.",
        "Campus Housing",
        new Dictionary<string, string>())
{
    protected override Task<McpToolExecution> ExecuteCoreAsync(
        McpCallRequest request,
        CancellationToken cancellationToken) =>
        campusHousing.GetRoomStatusAsync(cancellationToken);
}
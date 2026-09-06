using System.Net.Http.Json;

namespace EventScheduling.Backend.Clients;

public sealed class AcademicRecordsClient(HttpClient httpClient) : IAcademicRecordsClient
{
    public async Task<IReadOnlyList<AcademicRecordsUser>> GetUsersAsync(CancellationToken cancellationToken)
    {
        var users = await httpClient.GetFromJsonAsync<List<AcademicRecordsUser>>("/api/users", cancellationToken);
        return users ?? [];
    }
}

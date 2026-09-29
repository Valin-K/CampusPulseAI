using Microsoft.AspNetCore.Mvc;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IHttpClientFactory clients) : AcademicRecordsControllerBase(clients)
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var response = await AcademicRecordsDatabase.GetAsync("/api/users", cancellationToken);
        return await ForwardResponseAsync(response, cancellationToken);
    }
}

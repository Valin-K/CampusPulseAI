using System.Net;
using CampusPulse.Shared.Mcp.Client;
using Microsoft.AspNetCore.Http;

namespace AcademicRecords.Tests;

public sealed class McpAuthorizationHeaderHandlerTests
{
    [Test]
    public async Task ForwardsAuthorizationFromCurrentRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer demo-token";
        var handler = new McpAuthorizationHeaderHandler(new HttpContextAccessor
        {
            HttpContext = context
        })
        {
            InnerHandler = new CapturingHandler()
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://mcp.test/api/mcp/tools");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(((CapturingHandler)handler.InnerHandler!).Authorization, Is.EqualTo("Bearer demo-token"));
        });
    }

    [Test]
    public async Task LeavesAuthorizationUnsetWithoutCurrentRequest()
    {
        var handler = new McpAuthorizationHeaderHandler(new HttpContextAccessor())
        {
            InnerHandler = new CapturingHandler()
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://mcp.test/api/mcp/tools");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(((CapturingHandler)handler.InnerHandler!).Authorization, Is.Null);
        });
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

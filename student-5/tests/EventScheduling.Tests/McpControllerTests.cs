using System.Net;
using System.Text;
using EventScheduling.Backend.Controllers;
using EventScheduling.Backend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventScheduling.Tests;

public sealed class McpControllerTests
{
    [Test]
    public async Task VerifyIntegrationReturnsOkWithTheValidationResult()
    {
        const string json =
            """{"tool":"validate_integration","isError":false,"result":{"feature":"student-5","valid":true,"checks":["feature-allowlisted","tool-registered","structured-result"]},"executedAtUtc":"2026-09-27T00:00:00Z"}""";
        var controller = Controller(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

        var result = await controller.VerifyIntegration(CancellationToken.None);

        var payload = (result as OkObjectResult)?.Value as McpIntegrationResponse;
        Assert.Multiple(() =>
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Result.Feature, Is.EqualTo("student-5"));
            Assert.That(payload.Result.Valid, Is.True);
            Assert.That(payload.Result.Checks, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task VerifyIntegrationMapsServerOutageTo503()
    {
        var controller = Controller(new StubHandler(new HttpRequestException("mcp down")));

        var result = await controller.VerifyIntegration(CancellationToken.None);

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(503));
    }

    private static McpController Controller(HttpMessageHandler handler) =>
        new(new SingleClientFactory(handler), NullLogger<McpController>.Instance);

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler) { BaseAddress = new Uri("http://mcp.test/") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? response;
        private readonly Exception? exception;

        public StubHandler(HttpResponseMessage response) => this.response = response;

        public StubHandler(Exception exception) => this.exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            exception is not null
                ? Task.FromException<HttpResponseMessage>(exception)
                : Task.FromResult(response!);
    }
}

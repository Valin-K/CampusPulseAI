using System.Net;
using System.Text;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using EventScheduling.Backend.Models;
using EventScheduling.Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventScheduling.Tests;

public sealed class EventKnowledgeServiceTests
{
    [Test]
    public async Task AskRoutesMcpQuestionsToTheMcpToolInsteadOfRag()
    {
        const string json =
            """{"tool":"validate_integration","isError":false,"result":{"feature":"student-5","valid":true,"checks":["feature-allowlisted","tool-registered","structured-result"]},"executedAtUtc":"2026-09-27T00:00:00Z"}""";
        var mcp = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var service = CreateService(mcp);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "Is the MCP integrated?" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Reply, Does.Contain("integrated with the shared MCP server"));
            Assert.That(answer.Reply, Does.Contain("feature-allowlisted"));
            Assert.That(answer.ConfidenceCategory, Is.Empty);
            Assert.That(mcp.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AskReturnsAFriendlyMessageWhenTheMcpServerIsDown()
    {
        var service = CreateService(new StubHandler(new HttpRequestException("mcp down")));

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "is mcp connected?" }, CancellationToken.None);

        Assert.That(answer.Reply, Does.Contain("couldn't reach the shared MCP server"));
    }

    // A non-MCP question must never hit the MCP client; the unused RAG client throws if RAG is
    // reached, so this test also proves the MCP branch does not run for ordinary questions.
    private static EventKnowledgeService CreateService(HttpMessageHandler mcpHandler)
    {
        var factory = new SingleClientFactory(mcpHandler);
        var rag = new RagKnowledgeService(new UnusedRagServerClient(), new RagCitationParser());
        return new EventKnowledgeService(factory, rag, NullLogger<EventKnowledgeService>.Instance);
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler) { BaseAddress = new Uri("http://mcp.test/") };
    }

    private sealed class UnusedRagServerClient : IRagServerClient
    {
        public Task<RagIngestResponse?> IngestAsync(
            string collection, RagIngestRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("RAG should not be called for an MCP question.");

        public Task<RagAnswerResponse?> AnswerAsync(
            string collection, RagAnswerRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("RAG should not be called for an MCP question.");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? response;
        private readonly Exception? exception;

        public StubHandler(HttpResponseMessage response) => this.response = response;

        public StubHandler(Exception exception) => this.exception = exception;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return exception is not null
                ? Task.FromException<HttpResponseMessage>(exception)
                : Task.FromResult(response!);
        }
    }
}

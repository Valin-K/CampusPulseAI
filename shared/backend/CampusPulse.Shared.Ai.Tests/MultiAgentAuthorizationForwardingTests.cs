using System.Net;
using CampusPulse.Shared.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace CampusPulse.Shared.Ai.Tests;

public sealed class MultiAgentAuthorizationForwardingTests
{
    [Test]
    public async Task ForwardsCurrentAuthorizationToMultiAgentServer()
    {
        var handler = new CapturingHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiAgentCaller(new ConfigurationBuilder().Build());
        services.Configure<HttpClientFactoryOptions>("multiAgent", options =>
            options.HttpMessageHandlerBuilderActions.Add(
                builder => builder.PrimaryHandler = handler));
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer workflow-token";
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("multiAgent");
        using var response = await client.GetAsync("api/workflows");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(handler.Authorization, Is.EqualTo("Bearer workflow-token"));
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

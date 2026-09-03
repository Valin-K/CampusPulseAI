using CampusHousing.Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;

namespace CampusHousing.Tests;

public class MaintenanceClassifierTests
{
    [TestCase("There is a gas leak in the kitchen", "Emergency", "Plumbing")]
    [TestCase("The hallway light is broken", "Low", "Electrical")]
    [TestCase("The door lock is not working", "Low", "Security")]
    public async Task ClassifyAsync_WithoutGemini_UsesRuleBasedClassification(string description, string severity, string queue)
    {
        var result = await CreateClassifier().ClassifyAsync(description, CancellationToken.None);

        Assert.That(result.Severity, Is.EqualTo(severity));
        Assert.That(result.RepairQueue, Is.EqualTo(queue));
    }

    [TestCase("There is a flood and no heat", "Emergency", "Heating")]
    [TestCase("The broken heater", "High", "Heating")]
    [TestCase("The water is leaking", "High", "Plumbing")]
    [TestCase("The power is out", "Low", "Electrical")]
    [TestCase("The security camera is broken", "Low", "Security")]
    [TestCase("The window is stuck", "Low", "General Maintenance")]
    public async Task ClassifyAsync_FallbackRecognizesAllRuleCategories(string description, string severity, string queue)
    {
        var result = await CreateClassifier().ClassifyAsync(description, CancellationToken.None);

        Assert.That(result.Severity, Is.EqualTo(severity));
        Assert.That(result.RepairQueue, Is.EqualTo(queue));
    }

    [Test]
    public async Task ClassifyAsync_GeminiResponseIsNormalized()
    {
        var responseText = "```json\n{\"severity\":\" emergency \",\"repairQueue\":\" invalid \"}\n```";
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = responseText } } } } }
        }));

        var result = await classifier.ClassifyAsync("The issue is described", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("emergency", "General Maintenance")));
    }

    [TestCase(HttpStatusCode.BadGateway)]
    [TestCase(HttpStatusCode.OK)]
    public async Task ClassifyAsync_InvalidGeminiResponseUsesFallback(HttpStatusCode status)
    {
        var body = status == HttpStatusCode.OK ? new { candidates = Array.Empty<object>() } : null;
        var classifier = CreateClassifier(new StubHandler(status, body));

        var result = await classifier.ClassifyAsync("A gas leak", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Emergency", "Plumbing")));
    }

    [Test]
    public async Task ClassifyAsync_GeminiJsonExceptionUsesFallback()
    {
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, "not json"));

        var result = await classifier.ClassifyAsync("A broken light", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Low", "Electrical")));
    }

    [Test]
    public async Task ClassifyAsync_ValidGeminiValuesAreTrimmedAndCaseInsensitive()
    {
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "{\"Severity\":\" Medium \",\"RepairQueue\":\" security \"}" } } } } }
        }));

        var result = await classifier.ClassifyAsync("A camera issue", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Medium", "security")));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ClassifyAsync_MissingGeminiTextUsesFallback(string? text)
    {
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text } } } } }
        }));

        var result = await classifier.ClassifyAsync("A broken light", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Low", "Electrical")));
    }

    [TestCase(typeof(HttpRequestException))]
    [TestCase(typeof(TaskCanceledException))]
    public async Task ClassifyAsync_GeminiTransportFailureUsesFallback(Type exceptionType)
    {
        var classifier = CreateClassifier(new ThrowingHandler(exceptionType));

        var result = await classifier.ClassifyAsync("A gas leak", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Emergency", "Plumbing")));
    }

    [Test]
    public async Task ClassifyAsync_UsesConfiguredModelInGeminiPath()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "{\"severity\":\"Low\",\"repairQueue\":\"Security\"}" } } } } }
        });
        var classifier = CreateClassifier(handler, "gemini-custom");

        await classifier.ClassifyAsync("A lock issue", CancellationToken.None);

        Assert.That(handler.LastRequest!.RequestUri!.ToString(), Does.Contain("gemini-custom:generateContent"));
    }

    private static MaintenanceClassifier CreateClassifier(HttpMessageHandler? handler = null, string? model = null)
    {
        return new MaintenanceClassifier(
            new TestHttpClientFactory(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = handler is null ? null : "test-key",
                ["Gemini:Model"] = model
            }).Build(),
            NullLogger<MaintenanceClassifier>.Instance);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler? handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler ?? new StubHandler(HttpStatusCode.OK, null)) { BaseAddress = new Uri("https://gemini.test/") };
    }

    private sealed class StubHandler(HttpStatusCode status, object? body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body) });
        }
    }

    private sealed class ThrowingHandler(Type exceptionType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>((Exception)Activator.CreateInstance(exceptionType)!);
    }
}

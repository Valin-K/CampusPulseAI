using System.Net;
using System.Text.Json;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace CampusPulse.Shared.Ai.Tests;

public sealed class AiGatewayClientTests
{
    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://gateway.invalid/service/") };

    [Test]
    public async Task GenerateAsync_UsesFlatGatewayProtocolWithoutProviderCredentials()
    {
        using var handler = new RecordingHttpMessageHandler("""{"text":"Answer","model":"server-model"}""");
        using var http = Client(handler);
        var result = await new AiGatewayClient(http).GenerateAsync("Prompt", "Context");
        var request = handler.Requests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri.AbsoluteUri, Is.EqualTo("https://gateway.invalid/service/api/ai/generate"));
            Assert.That(request.Body.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "prompt", "context" }));
            Assert.That(request.Body.GetProperty("prompt").GetString(), Is.EqualTo("Prompt"));
            Assert.That(request.Body.GetProperty("context").GetString(), Is.EqualTo("Context"));
            Assert.That(request.Headers.ContainsKey("x-goog-api-key"), Is.False);
            Assert.That(request.Headers.ContainsKey("Authorization"), Is.False);
            Assert.That(request.Uri.Query, Is.Empty);
            Assert.That(result, Is.EqualTo(new AiGenerationResponse("Answer", "server-model")));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EmbedAsync_UsesFlatProtocolAndBatches101TextsInOrder(bool isQuery)
    {
        var offset = 0;
        using var handler = new RecordingHttpMessageHandler(async (request, token) =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var count = document.RootElement.GetProperty("texts").GetArrayLength();
            var vectors = Enumerable.Range(offset, count).Select(index => new float[] { index, -index }).ToArray();
            offset += count;
            return RecordingHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new
            {
                embeddings = vectors,
                model = "server-embedding-model"
            }));
        });
        using var http = Client(handler);
        var texts = Enumerable.Range(0, 101).Select(index => $"Text {index}").ToArray();
        var result = await new AiGatewayClient(http).EmbedAsync(texts, isQuery);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests.Select(request => request.Body.GetProperty("texts").GetArrayLength()),
                Is.EqualTo(new[] { 100, 1 }));
            Assert.That(handler.Requests.SelectMany(request => request.Body.GetProperty("texts")
                .EnumerateArray().Select(text => text.GetString())), Is.EqualTo(texts));
            foreach (var request in handler.Requests)
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(request.Uri.AbsoluteUri, Is.EqualTo("https://gateway.invalid/service/api/ai/embeddings"));
                Assert.That(request.Body.EnumerateObject().Select(property => property.Name),
                    Is.EquivalentTo(new[] { "texts", "isQuery" }));
                Assert.That(request.Body.GetProperty("isQuery").GetBoolean(), Is.EqualTo(isQuery));
                Assert.That(request.Headers.ContainsKey("x-goog-api-key"), Is.False);
                Assert.That(request.Headers.ContainsKey("Authorization"), Is.False);
            }
            Assert.That(result.Model, Is.EqualTo("server-embedding-model"));
            Assert.That(result.Embeddings, Is.EqualTo(
                Enumerable.Range(0, 101).Select(index => new float[] { index, -index }).ToArray()));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EmbedAsync_RejectsModelOrDimensionChangesAcrossBatches(bool changeModel)
    {
        var batch = 0;
        using var handler = new RecordingHttpMessageHandler((_, _) =>
        {
            var first = batch++ == 0;
            return Task.FromResult(RecordingHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new
            {
                embeddings = Enumerable.Range(0, first ? 100 : 1)
                    .Select(_ => !first && !changeModel ? new float[] { 1, 2, 3 } : [1, 2]).ToArray(),
                model = !first && changeModel ? "changed-model" : "original-model"
            })));
        });
        using var http = Client(handler);
        var client = new AiGatewayClient(http);

        Assert.ThrowsAsync<HttpRequestException>(
            () => client.EmbedAsync(Enumerable.Repeat("Text", 101).ToArray(), false));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [TestCase("{}")]
    [TestCase("""{"text":"","model":"model"}""")]
    [TestCase("""{"text":" ","model":"model"}""")]
    [TestCase("""{"text":null,"model":"model"}""")]
    [TestCase("""{"text":"Answer","model":""}""")]
    [TestCase("""{"text":"Answer","model":null}""")]
    [TestCase("""{"text":42,"model":"model"}""")]
    [TestCase("""{"candidates":[{"content":{"parts":[{"text":"Wrong protocol"}]}}]}""")]
    public void GenerateAsync_RejectsMalformedOrEmptyAnswer(string response)
    {
        using var handler = new RecordingHttpMessageHandler(response);
        using var http = Client(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => new AiGatewayClient(http).GenerateAsync("Prompt", "Context"));
    }

    [TestCase("{}")]
    [TestCase("""{"embeddings":null,"model":"model"}""")]
    [TestCase("""{"embeddings":[],"model":"model"}""")]
    [TestCase("""{"embeddings":[null],"model":"model"}""")]
    [TestCase("""{"embeddings":[[]],"model":"model"}""")]
    [TestCase("""{"embeddings":[[1,2],[3,4]],"model":"model"}""")]
    [TestCase("""{"embeddings":[[1,2]],"model":""}""")]
    [TestCase("""{"embeddings":[[1,2]],"model":null}""")]
    [TestCase("""{"embeddings":[[1e100,2]],"model":"model"}""")]
    [TestCase("""{"embeddings":[[-1e100,2]],"model":"model"}""")]
    [TestCase("""{"embeddings":[["NaN",2]],"model":"model"}""")]
    [TestCase("""{"embeddings":[["Infinity",2]],"model":"model"}""")]
    [TestCase("""{"embeddings":[{"values":[1,2]}],"model":"model"}""")]
    public void EmbedAsync_RejectsMalformedVectors(string response)
    {
        using var handler = new RecordingHttpMessageHandler(response);
        using var http = Client(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => new AiGatewayClient(http).EmbedAsync(["Text"], false));
    }

    [Test]
    public void EmbedAsync_RejectsInconsistentDimensionsWithinBatch()
    {
        using var handler = new RecordingHttpMessageHandler("""{"embeddings":[[1,2],[3]],"model":"model"}""");
        using var http = Client(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => new AiGatewayClient(http).EmbedAsync(["One", "Two"], false));
    }

    [Test]
    public async Task GetStatusAsync_UsesStatusEndpointAndReturnsServerConfiguration()
    {
        using var handler = new RecordingHttpMessageHandler(
            """{"generationAvailable":false,"generationModel":"generation","embeddingModel":"embedding","embeddingDimensions":768}""");
        using var http = Client(handler);
        var status = await new AiGatewayClient(http).GetStatusAsync();
        var request = handler.Requests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Uri.AbsoluteUri, Is.EqualTo("https://gateway.invalid/service/api/ai/status"));
            Assert.That(status, Is.EqualTo(new AiGatewayStatus(false, "generation", "embedding", 768)));
        });
    }

    [TestCase("{}")]
    [TestCase("""{"generationModel":"","embeddingModel":"embedding","embeddingDimensions":2}""")]
    [TestCase("""{"generationModel":"generation","embeddingModel":"","embeddingDimensions":2}""")]
    [TestCase("""{"generationModel":"generation","embeddingModel":"embedding","embeddingDimensions":0}""")]
    [TestCase("""{"generationModel":"generation","embeddingModel":"embedding","embeddingDimensions":-1}""")]
    public void GetStatusAsync_RejectsInvalidStatus(string response)
    {
        using var handler = new RecordingHttpMessageHandler(response);
        using var http = Client(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => new AiGatewayClient(http).GetStatusAsync());
    }

    [TestCase("generate")]
    [TestCase("embed")]
    [TestCase("status")]
    public void AllOperations_RejectHttpErrorsAndPreserveStatus(string operation)
    {
        using var handler = new RecordingHttpMessageHandler("private upstream details", HttpStatusCode.BadGateway);
        using var http = Client(handler);
        var exception = Assert.ThrowsAsync<HttpRequestException>(() => Invoke(new AiGatewayClient(http), operation));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(exception.Message, Does.Not.Contain("private upstream details"));
        });
    }

    [TestCase("generate", "null")]
    [TestCase("generate", "")]
    [TestCase("generate", "not json")]
    [TestCase("embed", "null")]
    [TestCase("embed", "")]
    [TestCase("embed", "not json")]
    [TestCase("status", "null")]
    [TestCase("status", "")]
    [TestCase("status", "not json")]
    public void AllOperations_RejectEmptyOrInvalidJson(string operation, string response)
    {
        using var handler = new RecordingHttpMessageHandler(response);
        using var http = Client(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => Invoke(new AiGatewayClient(http), operation));
    }

    [TestCase("generate")]
    [TestCase("embed")]
    [TestCase("status")]
    public void AllOperations_PropagateCancellationToTransport(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHttpMessageHandler(async (_, token) =>
        {
            Assert.That(token.CanBeCanceled, Is.True);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return RecordingHttpMessageHandler.JsonResponse("{}");
        });
        using var http = Client(handler);

        Assert.That(() => Invoke(new AiGatewayClient(http), operation, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void InvalidInputs_FailBeforeNetworkCalls()
    {
        using var handler = new RecordingHttpMessageHandler("{}");
        using var http = Client(handler);
        var client = new AiGatewayClient(http);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => client.GenerateAsync(null!, "Context"));
            Assert.ThrowsAsync<ArgumentException>(() => client.GenerateAsync(" ", "Context"));
            Assert.ThrowsAsync<ArgumentNullException>(() => client.GenerateAsync("Prompt", null!));
            Assert.ThrowsAsync<ArgumentNullException>(() => client.EmbedAsync(null!, false));
            Assert.ThrowsAsync<ArgumentException>(() => client.EmbedAsync([], false));
            Assert.ThrowsAsync<ArgumentException>(() => client.EmbedAsync(["Text", " "], false));
            Assert.ThrowsAsync<ArgumentException>(() => client.EmbedAsync([null!], false));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [TestCase("http://gateway.invalid/base")]
    [TestCase("http://gateway.invalid/base/")]
    public async Task AddAiGateway_UsesConfiguredUrlWithoutForwardingGeminiKey(string gatewayUrl)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:AiGateway"] = gatewayUrl,
            ["Gemini:ApiKey"] = "must-not-be-forwarded"
        }).Build();
        using var handler = new RecordingHttpMessageHandler("""{"text":"Answer","model":"server-model"}""");
        var services = new ServiceCollection();
        services.AddAiGateway(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IAiGatewayClient>().GenerateAsync("Prompt", "Context");
        var request = handler.Requests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Is.EqualTo("Answer"));
            Assert.That(request.Uri.AbsoluteUri, Is.EqualTo("http://gateway.invalid/base/api/ai/generate"));
            Assert.That(request.Headers.ContainsKey("x-goog-api-key"), Is.False);
            Assert.That(request.Headers.ContainsKey("Authorization"), Is.False);
            Assert.That(request.Headers.Values, Has.None.Contains("must-not-be-forwarded"));
            Assert.That(request.Body.GetRawText(), Does.Not.Contain("must-not-be-forwarded"));
        });
    }

    [Test]
    public async Task AddAiGateway_ForwardsCurrentAuthorizationHeader()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var handler = new RecordingHttpMessageHandler("""{"text":"Answer","model":"server-model"}""");
        var services = new ServiceCollection();
        services.AddAiGateway(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request.Headers.Authorization = "Bearer caller-token";

        await provider.GetRequiredService<IAiGatewayClient>().GenerateAsync("Prompt", "Context");

        Assert.That(handler.Requests.Single().Headers["Authorization"], Is.EqualTo("Bearer caller-token"));
    }

    [Test]
    public async Task AddAiGateway_ForwardsAmbientAuthorizationDuringBackgroundWork()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var handler = new RecordingHttpMessageHandler("""{"text":"Answer","model":"server-model"}""");
        var services = new ServiceCollection();
        services.AddAiGateway(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        using var provider = services.BuildServiceProvider();
        using var authorization = provider.GetRequiredService<AuthorizationRequestContext>().Push("Bearer background-token");

        await provider.GetRequiredService<IAiGatewayClient>().GenerateAsync("Prompt", "Context");

        Assert.That(handler.Requests.Single().Headers["Authorization"], Is.EqualTo("Bearer background-token"));
    }

    private static async Task Invoke(AiGatewayClient client, string operation, CancellationToken token = default)
    {
        switch (operation)
        {
            case "generate":
                await client.GenerateAsync("Prompt", "Context", token);
                break;
            case "embed":
                await client.EmbedAsync(["Text"], false, token);
                break;
            case "status":
                await client.GetStatusAsync(token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}

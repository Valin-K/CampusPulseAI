using System.Net;
using CampusPulse.Shared.Ai.Gateway;

namespace CampusPulse.Shared.Ai.Tests;

public sealed class GeminiServiceTests
{
    private const string Answer = """{"candidates":[{"content":{"parts":[{"text":"Answer"}]},"finishReason":"STOP"}]}""";
    private static GeminiOptions Options(string key = "server-only-test-key") => new()
    {
        ApiKey = key,
        GenerationModel = "server-generation-model",
        EmbeddingModel = "server-embedding-model",
        EmbeddingDimensions = 2
    };

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://gemini.invalid/") };

    [Test]
    public async Task GenerateAsync_SeparatesInstructionsFromContextAndUsesConfiguredModel()
    {
        using var handler = new RecordingHttpMessageHandler(Answer);
        using var http = Client(handler);
        var result = await new GeminiService(http, Options()).GenerateAsync(
            "Trusted instructions", "Untrusted context", CancellationToken.None);
        var request = handler.Requests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri.AbsoluteUri,
                Is.EqualTo("https://gemini.invalid/v1beta/models/server-generation-model:generateContent"));
            Assert.That(request.Body.GetProperty("systemInstruction").GetProperty("parts")[0]
                .GetProperty("text").GetString(), Is.EqualTo("Trusted instructions"));
            Assert.That(request.Body.GetProperty("contents").GetArrayLength(), Is.EqualTo(1));
            Assert.That(request.Body.GetProperty("contents")[0].GetProperty("parts")[0]
                .GetProperty("text").GetString(), Is.EqualTo("Untrusted context"));
            Assert.That(result, Is.EqualTo(new AiGenerationResponse("Answer", "server-generation-model")));
            Assert.That(request.Body.GetRawText(), Does.Not.Contain("server-only-test-key"));
        });
    }

    [TestCase("")]
    [TestCase(" \r\n")]
    public async Task GenerateAsync_EmptyContextUsesDefaultUserContent(string context)
    {
        using var handler = new RecordingHttpMessageHandler(Answer);
        using var http = Client(handler);
        await new GeminiService(http, Options()).GenerateAsync("Instructions", context, CancellationToken.None);

        Assert.That(handler.Requests.Single().Body.GetProperty("contents")[0]
            .GetProperty("parts")[0].GetProperty("text").GetString(), Is.EqualTo("Perform the requested task."));
    }

    [Test]
    public async Task GenerateAsync_ConcatenatesVisiblePartsTrimsAndIgnoresThoughtAndOtherCandidates()
    {
        const string response = """
            {"candidates":[
              {"content":{"parts":[{"text":"  Hello "},{"text":"private reasoning","thought":true},
                {"text":"world"},{"text":null},{"text":"!  ","thought":false}]},"finishReason":"STOP"},
              {"content":{"parts":[{"text":"Not the selected answer"}]},"finishReason":"STOP"}]}
            """;
        using var handler = new RecordingHttpMessageHandler(response);
        using var http = Client(handler);
        var result = await new GeminiService(http, Options()).GenerateAsync("Prompt", "Context", CancellationToken.None);

        Assert.That(result.Text, Is.EqualTo("Hello world!"));
    }

    [TestCase("""{"candidates":[{"content":{"parts":[{"text":"Unsafe"}]},"finishReason":"SAFETY"}]}""")]
    [TestCase("""{"candidates":[{"content":{"parts":[{"text":"Partial"}]},"finishReason":"MAX_TOKENS"}]}""")]
    [TestCase("""{"promptFeedback":{"blockReason":"SAFETY"}}""")]
    [TestCase("""{"candidates":[]}""")]
    [TestCase("""{"candidates":[null]}""")]
    [TestCase("""{"candidates":[{}]}""")]
    [TestCase("""{"candidates":[{"content":{"parts":[]}}]}""")]
    [TestCase("""{"candidates":[{"content":{"parts":[{"text":"  "}]}}]}""")]
    [TestCase("""{"candidates":[{"content":{"parts":[{"text":"Hidden","thought":true}]}}]}""")]
    [TestCase("""{"candidates":[{"content":{"parts":[null]}}]}""")]
    [TestCase("""{"candidates":"wrong type"}""")]
    [TestCase("""{"candidates":[{"content":{"parts":[{"text":42}]}}]}""")]
    [TestCase("{}")]
    [TestCase("null")]
    [TestCase("")]
    [TestCase("not json")]
    public void GenerateAsync_InvalidOrBlockedResponseThrows(string response)
    {
        using var handler = new RecordingHttpMessageHandler(response);
        using var http = Client(handler);
        var service = new GeminiService(http, Options());

        Assert.ThrowsAsync<HttpRequestException>(() => service.GenerateAsync("Prompt", "Context", CancellationToken.None));
    }

    [TestCase(false, "RETRIEVAL_DOCUMENT")]
    [TestCase(true, "RETRIEVAL_QUERY")]
    public async Task EmbedAsync_UsesServerModelDimensionsAndTaskType(bool isQuery, string taskType)
    {
        using var handler = new RecordingHttpMessageHandler(
            """{"embeddings":[{"values":[1.25,-2.5]},{"values":[0,0.75]}]}""");
        using var http = Client(handler);
        var result = await new GeminiService(http, Options()).EmbedAsync(
            ["First text", "Second text"], isQuery, CancellationToken.None);
        var request = handler.Requests.Single();
        var requests = request.Body.GetProperty("requests");

        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri.AbsoluteUri,
                Is.EqualTo("https://gemini.invalid/v1beta/models/server-embedding-model:batchEmbedContents"));
            Assert.That(requests.GetArrayLength(), Is.EqualTo(2));
            for (var index = 0; index < 2; index++)
            {
                Assert.That(requests[index].GetProperty("model").GetString(), Is.EqualTo("models/server-embedding-model"));
                Assert.That(requests[index].GetProperty("taskType").GetString(), Is.EqualTo(taskType));
                Assert.That(requests[index].GetProperty("outputDimensionality").GetInt32(), Is.EqualTo(2));
                Assert.That(requests[index].GetProperty("content").GetProperty("parts")[0]
                    .GetProperty("text").GetString(), Is.EqualTo(index == 0 ? "First text" : "Second text"));
            }
            Assert.That(result.Model, Is.EqualTo("server-embedding-model"));
            Assert.That(result.Embeddings, Is.EqualTo(new float[][] { [1.25f, -2.5f], [0f, 0.75f] }));
        });
    }

    [TestCase("{}")]
    [TestCase("null")]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("""{"embeddings":null}""")]
    [TestCase("""{"embeddings":[]}""")]
    [TestCase("""{"embeddings":[null]}""")]
    [TestCase("""{"embeddings":[{}]}""")]
    [TestCase("""{"embeddings":[{"values":null}]}""")]
    [TestCase("""{"embeddings":[{"values":[]}]}""")]
    [TestCase("""{"embeddings":[{"values":[1]}]}""")]
    [TestCase("""{"embeddings":[{"values":[1,2,3]}]}""")]
    [TestCase("""{"embeddings":[{"values":[1,2]},{"values":[3,4]}]}""")]
    [TestCase("""{"embeddings":[{"values":[1e100,2]}]}""")]
    [TestCase("""{"embeddings":[{"values":[-1e100,2]}]}""")]
    [TestCase("""{"embeddings":[{"values":["NaN",2]}]}""")]
    [TestCase("""{"embeddings":[{"values":["Infinity",2]}]}""")]
    [TestCase("""{"embeddings":[{"values":"wrong type"}]}""")]
    public void EmbedAsync_InvalidVectorsThrow(string response)
    {
        using var handler = new RecordingHttpMessageHandler(response);
        using var http = Client(handler);
        var service = new GeminiService(http, Options());

        Assert.ThrowsAsync<HttpRequestException>(() => service.EmbedAsync(["Text"], false, CancellationToken.None));
    }

    [TestCase("")]
    [TestCase(" \t")]
    public void MissingKey_PreventsAllNetworkCallsAndReportsUnavailable(string key)
    {
        using var handler = new RecordingHttpMessageHandler(Answer);
        using var http = Client(handler);
        var service = new GeminiService(http, Options(key));

        var generation = Assert.ThrowsAsync<HttpRequestException>(
            () => service.GenerateAsync("Prompt", "Context", CancellationToken.None));
        var embedding = Assert.ThrowsAsync<HttpRequestException>(
            () => service.EmbedAsync(["Text"], false, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(generation!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(embedding!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(service.Status.GenerationAvailable, Is.False);
            Assert.That(service.Status.GenerationModel, Is.EqualTo("server-generation-model"));
            Assert.That(service.Status.EmbeddingModel, Is.EqualTo("server-embedding-model"));
            Assert.That(service.Status.EmbeddingDimensions, Is.EqualTo(2));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [TestCase(false, HttpStatusCode.Unauthorized)]
    [TestCase(true, HttpStatusCode.TooManyRequests)]
    [TestCase(false, HttpStatusCode.InternalServerError)]
    [TestCase(true, HttpStatusCode.BadGateway)]
    public void NonSuccessResponse_PreservesStatusWithoutLeakingBody(bool embed, HttpStatusCode status)
    {
        using var handler = new RecordingHttpMessageHandler("provider secret details", status);
        using var http = Client(handler);
        var service = new GeminiService(http, Options());
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            if (embed)
                await service.EmbedAsync(["Text"], false, CancellationToken.None);
            else
                await service.GenerateAsync("Prompt", "Context", CancellationToken.None);
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(status));
            Assert.That(exception.Message, Does.Not.Contain("provider secret details"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Cancellation_IsPropagatedToHttpHandler(bool embed)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHttpMessageHandler(async (_, token) =>
        {
            Assert.That(token.CanBeCanceled, Is.True);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return RecordingHttpMessageHandler.JsonResponse(Answer);
        });
        using var http = Client(handler);
        var service = new GeminiService(http, Options());

        Assert.That(async () =>
        {
            if (embed)
                await service.EmbedAsync(["Text"], false, cancellation.Token);
            else
                await service.GenerateAsync("Prompt", "Context", cancellation.Token);
        }, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }
}

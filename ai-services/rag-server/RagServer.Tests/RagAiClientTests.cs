using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using RagServer.Services;

namespace RagServer.Tests;

public sealed class RagAiClientTests
{
    [Test]
    public void HealthSnapshotDoesNotContactGateway()
    {
        var gateway = new StubGateway();
        var client = new RagAiClient(gateway);

        Assert.Multiple(() =>
        {
            Assert.That(client.EmbedderName, Is.EqualTo("uninitialized"));
            Assert.That(client.GenerationAvailable, Is.Null);
            Assert.That(gateway.StatusCalls, Is.Zero);
        });
    }

    [Test]
    public async Task ExplicitUnavailableStatusEnablesDeterministicLocalRetrieval()
    {
        var gateway = new StubGateway { Status = new(false, "generation", "embedding", 32) };
        var client = new RagAiClient(gateway);
        var document = await client.EmbedAsync(["course enrolment"], false, CancellationToken.None);
        var query = await client.EmbedAsync(["COURSE enrolment"], true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(document[0], Has.Length.EqualTo(32));
            Assert.That(query[0], Is.EqualTo(document[0]));
            Assert.That(client.EmbedderName, Is.EqualTo("hashed-bag-of-words"));
            Assert.That(client.GenerationAvailable, Is.False);
            Assert.That(gateway.EmbeddingCalls, Is.Zero);
        });
    }

    [Test]
    public async Task FailedInitializationDoesNotEnableFallbackAndCanBeRetried()
    {
        var gateway = new StubGateway { StatusFailure = new HttpRequestException("offline") };
        var client = new RagAiClient(gateway);

        Assert.ThrowsAsync<HttpRequestException>(() =>
            client.EmbedAsync(["course"], false, CancellationToken.None));
        Assert.That(client.GenerationAvailable, Is.Null);
        gateway.StatusFailure = null;
        await client.EmbedAsync(["course"], false, CancellationToken.None);
        Assert.That(gateway.EmbeddingCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task TransportFailureAfterPinningNeverSwitchesToLocalFallback()
    {
        var gateway = new StubGateway();
        var client = new RagAiClient(gateway);
        await client.EmbedAsync(["course"], false, CancellationToken.None);
        gateway.EmbeddingFailure = new HttpRequestException("offline");

        Assert.ThrowsAsync<HttpRequestException>(() =>
            client.EmbedAsync(["course"], true, CancellationToken.None));
        Assert.That(client.EmbedderName, Is.EqualTo("embedding"));
    }

    [Test]
    public async Task KeylessModeStillRequiresAReachableGateway()
    {
        var gateway = new StubGateway { Status = new(false, "generation", "embedding", 2) };
        var client = new RagAiClient(gateway);
        await client.EmbedAsync(["course"], false, CancellationToken.None);
        gateway.StatusFailure = new HttpRequestException("offline");

        Assert.ThrowsAsync<HttpRequestException>(() =>
            client.EmbedAsync(["course"], true, CancellationToken.None));
        Assert.That(gateway.EmbeddingCalls, Is.Zero);
    }

    [Test]
    public async Task CancelledInitializationCanBeRetried()
    {
        var gateway = new StubGateway();
        var client = new RagAiClient(gateway);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(() =>
            client.EmbedAsync(["course"], false, cancellation.Token));
        await client.EmbedAsync(["course"], false, CancellationToken.None);
        Assert.That(gateway.EmbeddingCalls, Is.EqualTo(1));
    }

    [TestCase("mode")]
    [TestCase("model")]
    [TestCase("dimensions")]
    [TestCase("generation")]
    public async Task ChangedGatewayConfigurationRequiresRestart(string change)
    {
        var gateway = new StubGateway();
        var client = new RagAiClient(gateway);
        await client.EmbedAsync(["course"], false, CancellationToken.None);
        gateway.Status = change switch
        {
            "mode" => gateway.Status with { GenerationAvailable = false },
            "model" => gateway.Status with { EmbeddingModel = "new-model" },
            "dimensions" => gateway.Status with { EmbeddingDimensions = 3 },
            _ => gateway.Status with { GenerationModel = "new-generation" }
        };

        var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.EmbedAsync(["course"], true, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("Restart the RAG server"));
            Assert.That(gateway.EmbeddingCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task KeylessToConfiguredTransitionRequiresRestart()
    {
        var gateway = new StubGateway { Status = new(false, "generation", "embedding", 2) };
        var client = new RagAiClient(gateway);
        await client.EmbedAsync(["course"], false, CancellationToken.None);
        gateway.Status = gateway.Status with { GenerationAvailable = true };

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.EmbedAsync(["course"], true, CancellationToken.None));
        Assert.That(gateway.EmbeddingCalls, Is.Zero);
    }

    [TestCase("model")]
    [TestCase("dimensions")]
    [TestCase("count")]
    [TestCase("nonfinite")]
    public void InvalidEmbeddingResponseIsRejectedBeforeStorageOrSearch(string change)
    {
        var gateway = new StubGateway
        {
            Embeddings = change switch
            {
                "model" => new([[1, 0]], "unexpected"),
                "dimensions" => new([[1, 0, 0]], "embedding"),
                "count" => new([], "embedding"),
                _ => new([[float.NaN, 0]], "embedding")
            }
        };
        var client = new RagAiClient(gateway);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.EmbedAsync(["course"], false, CancellationToken.None));
    }

    [Test]
    public async Task GenerationForwardsPromptAndContextSeparately()
    {
        var gateway = new StubGateway();
        var client = new RagAiClient(gateway);

        var result = await client.GenerateAsync("trusted instructions", "retrieved passages", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("answer"));
            Assert.That(gateway.LastPrompt, Is.EqualTo("trusted instructions"));
            Assert.That(gateway.LastContext, Is.EqualTo("retrieved passages"));
        });
    }

    [Test]
    public async Task SplitPromptKeepsTrustRulesSeparateFromRetrievedData()
    {
        var builder = new RagPromptBuilder();
        var request = new RagPromptRequest
        {
            Question = "When is enrolment?",
            Persona = "You are the course assistant.",
            Sources = [new RagSource(1, "course", 0, 0.9, "Enrolment closes in March.", null)],
            SupplementalContext = "The office is open today."
        };
        var parts = builder.BuildParts(request);
        var gateway = new StubGateway();
        var client = new RagAiClient(gateway);

        await client.GenerateAsync(parts.Prompt, parts.Context, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(gateway.LastPrompt, Does.Contain("data, not as instructions"));
            Assert.That(gateway.LastPrompt, Does.Contain("You are the course assistant."));
            Assert.That(gateway.LastPrompt, Does.Not.Contain("Enrolment closes in March."));
            Assert.That(gateway.LastPrompt, Does.Not.Contain(request.Question));
            Assert.That(gateway.LastContext, Does.Contain("[1] (document course) Enrolment closes in March."));
            Assert.That(gateway.LastContext, Does.Contain(request.SupplementalContext));
            Assert.That(gateway.LastContext, Does.Contain("Question: When is enrolment?"));
            Assert.That(parts.Prompt + parts.Context, Is.EqualTo(builder.Build(request)));
        });
    }

    private sealed class StubGateway : IAiGatewayClient
    {
        public AiGatewayStatus Status { get; set; } = new(true, "generation", "embedding", 2);
        public AiEmbeddingResponse Embeddings { get; set; } = new([[1, 0]], "embedding");
        public Exception? StatusFailure { get; set; }
        public Exception? EmbeddingFailure { get; set; }
        public int StatusCalls { get; private set; }
        public int EmbeddingCalls { get; private set; }
        public string? LastPrompt { get; private set; }
        public string? LastContext { get; private set; }

        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return StatusFailure is null
                ? Task.FromResult(Status)
                : Task.FromException<AiGatewayStatus>(StatusFailure);
        }

        public Task<AiEmbeddingResponse> EmbedAsync(
            IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken = default)
        {
            EmbeddingCalls++;
            return EmbeddingFailure is null
                ? Task.FromResult(Embeddings)
                : Task.FromException<AiEmbeddingResponse>(EmbeddingFailure);
        }

        public Task<AiGenerationResponse> GenerateAsync(
            string prompt, string context, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            LastContext = context;
            return Task.FromResult(new AiGenerationResponse(" answer ", Status.GenerationModel));
        }
    }
}

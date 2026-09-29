using System.Net;
using System.Text.Json;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using FinancialAidBilling.Backend.Models;
using FinancialAidBilling.Backend.Services;

namespace FinancialAidBilling.Tests;

public sealed class FinancialAidKnowledgeServiceTests
{
    private static readonly ScholarshipResponse[] Scholarships =
    [
        new(12, "Precision Award", "Supports laboratory study", 1234.57m, 3.75m, "Science", 2,
            new DateOnly(2030, 11, 15), true),
        new(13, "Closed Award", "Historical award", 987.01m, 2.5m, "Any", 1,
            new DateOnly(2020, 1, 1), false)
    ];
    private const string Question = "  What scholarships are available?  ";

    [TestCase(RagStatus.Answered)]
    [TestCase(RagStatus.InsufficientContext)]
    [TestCase(RagStatus.GenerationUnavailable)]
    [TestCase(RagStatus.GenerationFailed)]
    public async Task ScholarshipQuestionIngestsDatabaseCorpusThenPreservesRagAnswerAndSources(string status)
    {
        var expected = Answer(status);
        using var harness = new Harness(answer: expected);
        var result = await harness.Service.AskScholarshipsAsync(new(Question), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Events, Is.EqualTo(new[] { "database", "ingest", "answer" }));
            Assert.That(harness.Database.Requests.Single().Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(harness.Database.Requests.Single().Path, Is.EqualTo("/api/scholarships"));
            Assert.That(harness.Rag.Requests.Select(request => request.Method),
                Is.EqualTo(new[] { HttpMethod.Post, HttpMethod.Post }));
            Assert.That(harness.Rag.Requests.Select(request => request.Path),
                Is.EqualTo(new[] { "/api/rag/financialaidbilling/documents", "/api/rag/financialaidbilling/answer" }));
            Assert.That(JsonSerializer.Serialize(result), Is.EqualTo(JsonSerializer.Serialize(expected)));
        });
        AssertCorpus(harness.Corpus());
        var answerRequest = harness.AnswerRequest();
        Assert.Multiple(() =>
        {
            Assert.That(answerRequest.Question, Is.EqualTo(Question.Trim()));
            Assert.That(answerRequest.TopK, Is.EqualTo(4));
            Assert.That(answerRequest.Instructions, Does.Contain("Cite your scholarship sources"));
            Assert.That(answerRequest.Instructions, Does.Contain("Do not confirm personal GPA eligibility"));
        });
    }

    [Test]
    public async Task SharedAskUsesIdenticalDatabaseCorpusAndCollection()
    {
        using var scholarshipHarness = new Harness();
        using var sharedHarness = new Harness();
        await scholarshipHarness.Service.AskScholarshipsAsync(new(Question), CancellationToken.None);
        var result = await sharedHarness.Service.AskAsync(new(Question), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(sharedHarness.Events, Is.EqualTo(new[] { "database", "ingest", "answer" }));
            Assert.That(sharedHarness.Rag.Requests[0].Body, Is.EqualTo(scholarshipHarness.Rag.Requests[0].Body));
            Assert.That(sharedHarness.Rag.Requests.Select(request => request.Path),
                Is.EqualTo(scholarshipHarness.Rag.Requests.Select(request => request.Path)));
            Assert.That(sharedHarness.AnswerRequest().Question, Is.EqualTo(Question.Trim()));
            Assert.That(sharedHarness.AnswerRequest().TopK, Is.EqualTo(4));
            Assert.That(result.Status, Is.EqualTo(RagStatus.Answered));
            Assert.That(result.Question, Is.EqualTo(Question.Trim()));
            Assert.That(result.Confidence, Is.EqualTo(0.87));
            Assert.That(result.Embedder, Is.EqualTo("test-embedder"));
            Assert.That(result.Sources.Single().DocumentId, Is.EqualTo("scholarship-12"));
            Assert.That(result.Sources.Single().Label, Is.EqualTo("Precision Award"));
        });
        AssertCorpus(sharedHarness.Corpus());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task EmptyScholarshipDatabaseStillIngestsPolicy(bool shared)
    {
        using var harness = new Harness(failStage: "database", failure: "empty");
        await harness.Ask(shared, CancellationToken.None);
        Assert.That(harness.Corpus().Documents.Select(document => document.Id),
            Is.EqualTo(new[] { "policy-scholarships" }));
    }

    [TestCaseSource(nameof(FailureCases))]
    public void DependencyFailureIsNotTurnedIntoAnAnswer(bool shared, string stage, string failure)
    {
        using var harness = new Harness(failStage: stage, failure: failure);
        Assert.That(async () => await harness.Ask(shared, CancellationToken.None), Throws.Exception);
        var expectedEvents = stage switch
        {
            "database" => new[] { "database" },
            "ingest" => new[] { "database", "ingest" },
            _ => new[] { "database", "ingest", "answer" }
        };
        Assert.That(harness.Events, Is.EqualTo(expectedEvents));
    }

    private static IEnumerable<TestCaseData> FailureCases()
    {
        foreach (var shared in new[] { false, true })
        foreach (var stage in new[] { "database", "ingest", "answer" })
        foreach (var failure in new[] { "http", "network", "null", "malformed", "wrong-shape", "timeout" })
            yield return new TestCaseData(shared, stage, failure)
                .SetName($"{(shared ? "SharedAsk" : "ScholarshipAsk")}_{stage}_{failure}_FailsClosed");
    }

    [TestCase("ingest", "wrong-collection")]
    [TestCase("ingest", "wrong-document-count")]
    [TestCase("ingest", "zero-chunks")]
    [TestCase("ingest", "negative-chunks")]
    [TestCase("answer", "wrong-collection")]
    [TestCase("answer", "null-sources")]
    [TestCase("answer", "unknown-status")]
    [TestCase("answer", "null-status")]
    [TestCase("answer", "null-answer")]
    [TestCase("answer", "blank-answer")]
    [TestCase("answer", "empty-answer")]
    public void SuccessShapedInvalidRagResponseFailsClosed(string stage, string failure)
    {
        using var harness = new Harness(failStage: stage, failure: failure);
        Assert.That(async () => await harness.Service.AskScholarshipsAsync(new(Question), CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(harness.Events, Is.EqualTo(stage == "ingest"
            ? new[] { "database", "ingest" }
            : new[] { "database", "ingest", "answer" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreCanceledRequestDoesNotCallAnyDependency(bool shared)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var harness = new Harness();
        Assert.That(async () => await harness.Ask(shared, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(harness.Events, Is.Empty);
    }

    [TestCase(false, "database")]
    [TestCase(false, "ingest")]
    [TestCase(false, "answer")]
    [TestCase(true, "database")]
    [TestCase(true, "ingest")]
    [TestCase(true, "answer")]
    public async Task InFlightCancellationStopsAtCurrentDependency(bool shared, string stage)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var harness = new Harness(failStage: stage, failure: "cancel", entered: entered);
        var pending = harness.Ask(shared, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(harness.Events.Last(), Is.EqualTo(stage));
        Assert.That(harness.Events.Count, Is.EqualTo(stage == "database" ? 1 : stage == "ingest" ? 2 : 3));
    }

    private static void AssertCorpus(RagIngestRequest corpus)
    {
        var award = corpus.Documents.Single(document => document.Id == "scholarship-12");
        var closed = corpus.Documents.Single(document => document.Id == "scholarship-13");
        var policy = corpus.Documents.Single(document => document.Id == "policy-scholarships");
        Assert.Multiple(() =>
        {
            Assert.That(corpus.Documents, Has.Count.EqualTo(3));
            Assert.That(award.Text, Does.Contain("Precision Award. Supports laboratory study"));
            Assert.That(award.Text, Does.Contain("Award amount: $1234.57."));
            Assert.That(award.Text, Does.Contain("Minimum GPA (4-point scale): 3.75."));
            Assert.That(award.Text, Does.Contain("Eligible major: Science."));
            Assert.That(award.Text, Does.Contain("Minimum year level: 2."));
            Assert.That(award.Text, Does.Contain("November 15, 2030"));
            Assert.That(award.Text, Does.Contain("Applications are open."));
            Assert.That(award.Metadata!["title"], Is.EqualTo("Precision Award"));
            Assert.That(award.Metadata["scholarshipName"], Is.EqualTo("Precision Award"));
            Assert.That(closed.Text, Does.Contain("Award amount: $987.01."));
            Assert.That(closed.Text, Does.Contain("Applications are closed."));
            Assert.That(policy.Metadata!["title"], Is.EqualTo("Scholarships"));
            Assert.That(policy.Text, Does.Contain("deadline has not passed"));
            Assert.That(policy.Text, Does.Contain("4-point scale"));
            Assert.That(policy.Text, Does.Contain("7-point GPA scale"));
            Assert.That(policy.Text, Does.Contain("no conversion policy is configured"));
            Assert.That(policy.Text, Does.Contain("personal GPA eligibility requires aid-office review"));
        });
    }

    private static RagAnswerResponse Answer(string status = RagStatus.Answered) =>
        new("financialaidbilling", Question.Trim(), status,
            status == RagStatus.Answered ? "Precision Award provides $1234.57 [1]." : null,
            0.87,
            [new(1, "scholarship-12", 2, 0.93, "Award amount: $1234.57.",
                new Dictionary<string, string> { ["title"] = "Precision Award", ["scholarshipName"] = "Precision Award" })],
            "test-embedder", "preserved detail", new DateTimeOffset(2026, 9, 1, 1, 2, 3, TimeSpan.Zero));

    private sealed class Harness : IDisposable
    {
        private readonly HttpClient _databaseClient;
        private readonly HttpClient _ragClient;
        public List<string> Events { get; } = [];
        public RecordingHttpHandler Database { get; }
        public RecordingHttpHandler Rag { get; }
        public FinancialAidKnowledgeService Service { get; }

        public Harness(RagAnswerResponse? answer = null, string? failStage = null, string? failure = null,
            TaskCompletionSource? entered = null)
        {
            async Task<HttpResponseMessage> Respond(string stage, CancellationToken token, int documentCount = 3)
            {
                Events.Add(stage);
                if (stage == failStage)
                {
                    switch (failure)
                    {
                        case "http": return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                        case "network": throw new HttpRequestException("Dependency offline");
                        case "timeout": throw new TaskCanceledException("Dependency timeout", new TimeoutException());
                        case "null": return TestHttp.Raw("null");
                        case "malformed": return TestHttp.Raw("{");
                        case "wrong-shape": return TestHttp.Raw("{}");
                        case "wrong-collection":
                            return stage == "ingest"
                                ? TestHttp.Json(new RagIngestResponse("wrong-feature", documentCount, documentCount, "test-embedder"))
                                : TestHttp.Json(Answer() with { Collection = "wrong-feature" });
                        case "wrong-document-count":
                            return TestHttp.Json(new RagIngestResponse("financialaidbilling", documentCount + 1, documentCount, "test-embedder"));
                        case "zero-chunks":
                        case "negative-chunks":
                            return TestHttp.Json(new RagIngestResponse("financialaidbilling", documentCount,
                                failure == "zero-chunks" ? 0 : -1, "test-embedder"));
                        case "null-sources": return TestHttp.Json(Answer() with { Sources = null! });
                        case "unknown-status": return TestHttp.Json(Answer() with { Status = "unexpected" });
                        case "null-status": return TestHttp.Json(Answer() with { Status = null! });
                        case "null-answer": return TestHttp.Json(Answer() with { Answer = null });
                        case "blank-answer": return TestHttp.Json(Answer() with { Answer = " \t\r\n " });
                        case "empty-answer": return TestHttp.Json(Answer() with { Answer = "" });
                        case "empty": return TestHttp.Raw("[]");
                        case "cancel":
                            entered!.SetResult();
                            await Task.Delay(Timeout.Infinite, token);
                            break;
                    }
                }
                return stage switch
                {
                    "database" => TestHttp.Json(Scholarships),
                    "ingest" => TestHttp.Json(new RagIngestResponse("financialaidbilling", documentCount, documentCount, "test-embedder")),
                    _ => TestHttp.Json(answer ?? Answer())
                };
            }
            Database = new RecordingHttpHandler((_, token) => Respond("database", token));
            Rag = new RecordingHttpHandler(async (request, token) =>
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("/documents"))
                    return await Respond("answer", token);
                var ingest = JsonSerializer.Deserialize<RagIngestRequest>(
                    await request.Content!.ReadAsStringAsync(token), TestHttp.JsonOptions)!;
                return await Respond("ingest", token, ingest.Documents.Count);
            });
            _databaseClient = TestHttp.Client(Database);
            _ragClient = TestHttp.Client(Rag);
            var rag = new RagServerClient(_ragClient);
            Service = new FinancialAidKnowledgeService(new TestDatabaseFactory(_databaseClient),
                new RagKnowledgeService(rag, new RagCitationParser()), rag);
        }

        public async Task Ask(bool shared, CancellationToken token)
        {
            if (shared)
                await Service.AskAsync(new(Question), token);
            else
                await Service.AskScholarshipsAsync(new(Question), token);
        }

        public RagIngestRequest Corpus() =>
            JsonSerializer.Deserialize<RagIngestRequest>(Rag.Requests[0].Body!, TestHttp.JsonOptions)!;

        public RagAnswerRequest AnswerRequest() =>
            JsonSerializer.Deserialize<RagAnswerRequest>(Rag.Requests[1].Body!, TestHttp.JsonOptions)!;

        public void Dispose()
        {
            _databaseClient.Dispose();
            _ragClient.Dispose();
        }
    }
}

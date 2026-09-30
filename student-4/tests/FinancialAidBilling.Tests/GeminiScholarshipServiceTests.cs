using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using FinancialAidBilling.Backend.Models;
using FinancialAidBilling.Backend.Services;

namespace FinancialAidBilling.Tests;

public sealed class GeminiScholarshipServiceTests
{
    private const string AcademicId = "STU-0042";
    private static readonly StudentProfile Profile = new(4, "Student Name", "Computer Science", 2, AcademicId);
    private static readonly DateTimeOffset ExecutedAt = new(2026, 9, 1, 1, 2, 3, TimeSpan.Zero);

    [Test]
    public async Task RecommendUsesRealMcpHttpCallWithAcademicIdAndNestedGpa()
    {
        using var harness = new Harness();
        var response = await harness.Service.RecommendAsync(Profile, CancellationToken.None);
        var call = harness.Mcp.Requests.Single();
        using var payload = JsonDocument.Parse(call.Body!);
        var arguments = payload.RootElement.GetProperty("arguments");

        Assert.Multiple(() =>
        {
            Assert.That(call.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(call.Path, Is.EqualTo("/api/mcp/tools/call"));
            Assert.That(payload.RootElement.GetProperty("tool").GetString(),
                Is.EqualTo("calculate_student_academic_statistics"));
            Assert.That(arguments.EnumerateObject().Select(property => property.Name),
                Is.EqualTo(new[] { "student_id" }));
            Assert.That(arguments.GetProperty("student_id").GetString(), Is.EqualTo(AcademicId));
            Assert.That(arguments.GetProperty("student_id").GetString(), Is.Not.EqualTo(Profile.StudentId.ToString()));
            Assert.That(response.AcademicContext.StudentId, Is.EqualTo(AcademicId));
            Assert.That(response.AcademicContext.Gpa, Is.EqualTo(6.25m));
            Assert.That(response.AcademicContext.GpaScale, Is.EqualTo(7));
            Assert.That(response.AcademicContext.Tool, Is.EqualTo(ScholarshipAcademicService.StatisticsTool));
            Assert.That(response.AcademicContext.ExecutedAtUtc, Is.EqualTo(ExecutedAt));
            Assert.That(response.AcademicContext.GpaEligibility, Does.StartWith("Unverified:"));
            Assert.That(harness.Gateway.Context, Does.Contain("GPA (7-point scale): 6.25"));
            Assert.That(harness.Gateway.Context, Does.Contain("4-point GPA"));
            Assert.That(harness.Gateway.Context, Does.Contain("no conversion has been applied"));
            Assert.That(harness.Gateway.Prompt, Does.Contain("Never compare or convert these scales"));
            Assert.That(harness.Gateway.Prompt, Does.Contain("never claim GPA eligibility or ineligibility"));
            Assert.That(harness.Gateway.Prompt, Does.Contain("minimum GPA (4-point scale)"));
            Assert.That(harness.Gateway.Context, Does.Contain("aid-office review").IgnoreCase);
            Assert.That(harness.Events, Is.EqualTo(new[] { "mcp", "database", "gateway" }));
        });
    }

    [Test]
    public async Task RecommendPreservesEligibilityDeadlineBoundaryAndDescendingAwardOrder()
    {
        using var harness = new Harness();
        var response = await harness.Service.RecommendAsync(Profile, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(response.StudentId, Is.EqualTo(4));
            Assert.That(response.MatchCount, Is.EqualTo(3));
            Assert.That(response.Recommendation, Is.EqualTo("Scholarship recommendation"));
            Assert.That(harness.Gateway.Prompt, Does.Contain("Recommend scholarships only"));
            Assert.That(harness.Gateway.Prompt, Does.Not.Contain("Student Name"));
            Assert.That(harness.Gateway.Context, Does.Contain("Student Name"));
            Assert.That(harness.Gateway.Context, Does.Contain("Today Award"));
            Assert.That(harness.Gateway.Context, Does.Contain("STEM Award"));
            Assert.That(harness.Gateway.Context, Does.Contain("General Award"));
            Assert.That(harness.Gateway.Context, Does.Not.Contain("Excluded Award"));
            Assert.That(harness.Gateway.Context, Does.Contain("$2,000.37"));
            Assert.That(harness.Gateway.Context!.IndexOf("Today Award", StringComparison.Ordinal),
                Is.LessThan(harness.Gateway.Context.IndexOf("STEM Award", StringComparison.Ordinal)));
            Assert.That(harness.Gateway.Context.IndexOf("STEM Award", StringComparison.Ordinal),
                Is.LessThan(harness.Gateway.Context.IndexOf("General Award", StringComparison.Ordinal)));
            Assert.That(harness.Database.Requests.Single().Path, Is.EqualTo("/api/scholarships"));
            Assert.That(harness.Database.Requests.Single().Method, Is.EqualTo(HttpMethod.Get));
        });
    }

    [TestCase("computer science", "COMPUTER SCIENCE", true)]
    [TestCase("Engineering", "stem", true)]
    [TestCase("Information Technology", "STEM", true)]
    [TestCase("Mathematics", "STEM", true)]
    [TestCase("Music", "STEM", false)]
    [TestCase("Music", "ANY", true)]
    public async Task RecommendPreservesMajorRules(string major, string eligibleMajor, bool matches)
    {
        using var harness = new Harness(scholarships:
            [Awards()[0] with { EligibleMajor = eligibleMajor }]);
        var result = await harness.Service.RecommendAsync(Profile with { Major = major }, CancellationToken.None);
        Assert.That(result.MatchCount, Is.EqualTo(matches ? 1 : 0));
        Assert.That(harness.Gateway.Calls, Is.EqualTo(matches ? 1 : 0));
    }

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(7)]
    public async Task RecommendAcceptsUnavailableAndBoundaryGpaWithoutGpaFiltering(int? gpa)
    {
        using var harness = new Harness(mcpBody: McpBody(gpa));
        var result = await harness.Service.RecommendAsync(Profile, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.AcademicContext.Gpa, Is.EqualTo(gpa));
            Assert.That(result.MatchCount, Is.EqualTo(3));
            Assert.That(result.AcademicContext.GpaEligibility, Does.StartWith("Unverified:"));
            Assert.That(harness.Gateway.Context, Does.Contain(gpa is null
                ? "GPA (7-point scale): unavailable"
                : $"GPA (7-point scale): {gpa}.00"));
        });
        if (gpa is null)
        {
            Assert.That(harness.Gateway.Context, Does.Not.Contain("GPA (7-point scale): 0.00"));
            Assert.That(result.AcademicContext.GpaEligibility, Does.Contain("no finalized GPA"));
        }
    }

    [Test]
    public async Task NoMatchesReturnsUnverifiedContextWithoutCallingGateway()
    {
        using var harness = new Harness(scholarships: Awards().Where(award => award.Name.StartsWith("Excluded")).ToArray());
        var result = await harness.Service.RecommendAsync(Profile, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.MatchCount, Is.Zero);
            Assert.That(result.Recommendation, Does.Contain("GPA eligibility remains unverified"));
            Assert.That(result.AcademicContext.Gpa, Is.EqualTo(6.25m));
            Assert.That(harness.Gateway.Calls, Is.Zero);
        });
    }

    [TestCaseSource(nameof(InvalidMcpResponses))]
    public void InvalidMcpResponseFailsBeforeDatabaseOrGateway(string body)
    {
        using var harness = new Harness(mcpBody: body);
        Assert.That(async () => await harness.Service.RecommendAsync(Profile, CancellationToken.None),
            Throws.Exception);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Database.Requests, Is.Empty);
            Assert.That(harness.Gateway.Calls, Is.Zero);
        });
    }

    private static IEnumerable<TestCaseData> InvalidMcpResponses()
    {
        foreach (var (name, body) in new[]
        {
            ("MalformedJson", "{"), ("NullEnvelope", "null"), ("MissingEnvelope", "{}")
        })
            yield return new TestCaseData(body).SetName($"McpRejects{name}");

        foreach (var mutation in new[] { "wrong-tool", "missing-tool", "tool-error", "missing-timestamp",
            "default-timestamp", "missing-result", "null-result",
            "wrong-student", "missing-student", "missing-gpa", "null-gpa", "flat-gpa",
            "wrong-gpa-student", "missing-gpa-student", "wrong-statistic", "missing-statistic",
            "missing-value", "string-value", "negative-value", "too-large-value" })
        {
            var node = JsonNode.Parse(McpBody())!;
            var result = node["result"]!;
            var gpa = result["gpa"]!;
            switch (mutation)
            {
                case "wrong-tool": node["tool"] = "list_students"; break;
                case "missing-tool": node.AsObject().Remove("tool"); break;
                case "tool-error": node["isError"] = true; break;
                case "missing-timestamp": node.AsObject().Remove("executedAtUtc"); break;
                case "default-timestamp": node["executedAtUtc"] = "0001-01-01T00:00:00+00:00"; break;
                case "missing-result": node.AsObject().Remove("result"); break;
                case "null-result": node["result"] = null; break;
                case "wrong-student": result["studentId"] = "4"; break;
                case "missing-student": result.AsObject().Remove("studentId"); break;
                case "missing-gpa": result.AsObject().Remove("gpa"); break;
                case "null-gpa": result["gpa"] = null; break;
                case "flat-gpa": result["gpa"] = 6.25m; break;
                case "wrong-gpa-student": gpa["studentId"] = "OTHER"; break;
                case "missing-gpa-student": gpa.AsObject().Remove("studentId"); break;
                case "wrong-statistic": gpa["statistic"] = "WAM"; break;
                case "missing-statistic": gpa.AsObject().Remove("statistic"); break;
                case "missing-value": gpa.AsObject().Remove("value"); break;
                case "string-value": gpa["value"] = "not-a-number"; break;
                case "negative-value": gpa["value"] = -0.01m; break;
                case "too-large-value": gpa["value"] = 7.01m; break;
            }
            yield return new TestCaseData(node.ToJsonString()).SetName($"McpRejects_{mutation}");
        }
    }

    [TestCase("")]
    [TestCase(" ")]
    public void MissingAcademicIdFailsBeforeHttp(string academicId)
    {
        using var harness = new Harness();
        Assert.That(async () => await harness.Service.RecommendAsync(Profile with { AcademicStudentId = academicId }, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(harness.Mcp.Requests, Is.Empty);
        Assert.That(harness.Gateway.Calls, Is.Zero);
    }

    [TestCase("http")]
    [TestCase("network")]
    [TestCase("timeout")]
    public void McpTransportFailureNeverFallsBackToGateway(string failure)
    {
        using var harness = new Harness(mcpRespond: (_, _) => failure switch
        {
            "http" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)),
            "network" => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")),
            _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()))
        });
        Assert.That(async () => await harness.Service.RecommendAsync(Profile, CancellationToken.None),
            failure == "timeout" ? Throws.InstanceOf<OperationCanceledException>() : Throws.TypeOf<McpResponseException>());
        Assert.That(harness.Database.Requests, Is.Empty);
        Assert.That(harness.Gateway.Calls, Is.Zero);
    }

    [Test]
    public void ActualMcpHttpClientTimeoutStopsBeforeDatabaseAndGateway()
    {
        using var harness = new Harness(mcpTimeout: TimeSpan.FromMilliseconds(100),
            mcpRespond: async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return TestHttp.Raw(McpBody());
            });
        Assert.That(async () => await harness.Service.RecommendAsync(Profile, CancellationToken.None),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(harness.Database.Requests, Is.Empty);
        Assert.That(harness.Gateway.Calls, Is.Zero);
    }

    [TestCase("http")]
    [TestCase("null")]
    [TestCase("malformed")]
    public void ScholarshipDatabaseFailureNeverCallsGateway(string failure)
    {
        using var harness = new Harness(databaseRespond: (_, _) => Task.FromResult(failure switch
        {
            "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "null" => TestHttp.Raw("null"),
            _ => TestHttp.Raw("{")
        }));
        Assert.That(async () => await harness.Service.RecommendAsync(Profile, CancellationToken.None),
            Throws.Exception);
        Assert.That(harness.Mcp.Requests, Has.Count.EqualTo(1));
        Assert.That(harness.Gateway.Calls, Is.Zero);
    }

    [Test]
    public void RecommendPropagatesGatewayFailure()
    {
        using var harness = new Harness(gatewayFail: true);
        Assert.That(async () => await harness.Service.RecommendAsync(Profile, CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
        Assert.That(harness.Gateway.Calls, Is.EqualTo(1));
    }

    [Test]
    public void RecommendPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var harness = new Harness();
        Assert.That(async () => await harness.Service.RecommendAsync(Profile, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(harness.Gateway.Calls, Is.Zero);
    }

    [Test]
    public async Task CancellationDuringMcpPropagatesWithoutGateway()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var harness = new Harness(mcpRespond: async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return TestHttp.Raw(McpBody());
        });
        var pending = harness.Service.RecommendAsync(Profile, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(harness.Database.Requests, Is.Empty);
        Assert.That(harness.Gateway.Calls, Is.Zero);
    }

    private sealed class RecordingGateway(List<string> events, bool fail) : IAiGatewayClient
    {
        public Task<AiEmbeddingResponse> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string? Prompt { get; private set; }
        public string? Context { get; private set; }
        public int Calls { get; private set; }

        public Task<AiGenerationResponse> GenerateAsync(string prompt, string context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("gateway");
            Calls++;
            Prompt = prompt;
            Context = context;
            return fail
                ? Task.FromException<AiGenerationResponse>(new HttpRequestException("Gateway unavailable"))
                : Task.FromResult(new AiGenerationResponse("Scholarship recommendation", "gateway-test"));
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly HttpClient _mcpClient;
        private readonly HttpClient _databaseClient;
        public List<string> Events { get; } = [];
        public RecordingHttpHandler Mcp { get; }
        public RecordingHttpHandler Database { get; }
        public RecordingGateway Gateway { get; }
        public GeminiScholarshipService Service { get; }

        public Harness(string? mcpBody = null, ScholarshipResponse[]? scholarships = null,
            bool gatewayFail = false,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? mcpRespond = null,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? databaseRespond = null,
            TimeSpan? mcpTimeout = null)
        {
            Mcp = new RecordingHttpHandler((request, token) =>
            {
                Events.Add("mcp");
                return mcpRespond?.Invoke(request, token) ?? Task.FromResult(TestHttp.Raw(mcpBody ?? McpBody()));
            });
            Database = new RecordingHttpHandler((request, token) =>
            {
                Events.Add("database");
                return databaseRespond?.Invoke(request, token)
                    ?? Task.FromResult(TestHttp.Json(scholarships ?? Awards()));
            });
            _mcpClient = TestHttp.Client(Mcp);
            if (mcpTimeout.HasValue)
                _mcpClient.Timeout = mcpTimeout.Value;
            _databaseClient = TestHttp.Client(Database);
            Gateway = new RecordingGateway(Events, gatewayFail);
            Service = new GeminiScholarshipService(new TestDatabaseFactory(_databaseClient), Gateway,
                new ScholarshipAcademicService(new McpClient(_mcpClient)));
        }

        public void Dispose()
        {
            _mcpClient.Dispose();
            _databaseClient.Dispose();
        }
    }

    private static string McpBody(decimal? value = 6.25m) => JsonSerializer.Serialize(new
    {
        tool = ScholarshipAcademicService.StatisticsTool,
        isError = false,
        result = new
        {
            studentId = AcademicId,
            gpa = new { studentId = AcademicId, statistic = "GPA", value },
            wam = new { studentId = AcademicId, statistic = "WAM", value = 84m }
        },
        executedAtUtc = ExecutedAt
    });

    private static ScholarshipResponse[] Awards()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return
        [
            new(1, "General Award", "Any major", 1000, 2, "Any", 1, today.AddDays(10), true),
            new(2, "STEM Award", "STEM major", 2000.37m, 3, "STEM", 2, today.AddDays(20), true),
            new(3, "Excluded Award: year", "", 3000, 3, "Any", 4, today.AddDays(10), true),
            new(4, "Excluded Award: inactive", "", 3000, 3, "Any", 1, today.AddDays(10), false),
            new(5, "Excluded Award: major", "", 3000, 3, "Music", 1, today.AddDays(10), true),
            new(6, "Excluded Award: expired", "", 9000, 3, "Any", 1, today.AddDays(-1), true),
            new(7, "Today Award", "Deadline boundary", 4000, 4, "computer science", 2, today, true)
        ];
    }
}
